using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Jarvis.Core.Nlu;

public class OnnxEmbeddingModel : IEmbeddingModel, IDisposable
{
    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;

    public OnnxEmbeddingModel(string modelPath, string tokenizerPath)
    {
        _session = new InferenceSession(modelPath);
        using var tokenizerStream = File.OpenRead(tokenizerPath);
        // addBeginningOfSentence/addEndOfSentence оставлены false: библиотека вставляет их по
        // "сырым" ID самой .model-модели (bos=1, без eos), а нам нужны ID в раскладке HF/fairseq
        // XLM-RoBERTa (bos=0, pad=1, eos=2, unk=3, остальные токены — raw_id+1), под которую
        // реально обучен multilingual-e5-small. Обёртка и сдвиг делаются вручную в Embed —
        // см. RemapToFairseqIds. Эмпирически проверено на скачанной модели (см.
        // OnnxEmbeddingModelIntegrationTests): без этого сдвига косинусное сходство не отличает
        // похожие фразы от случайных (см. запаркованный риск в roadmap).
        _tokenizer = SentencePieceTokenizer.Create(tokenizerStream, addBeginningOfSentence: false, addEndOfSentence: false);
    }

    public static bool FilesExist(string modelPath, string tokenizerPath) =>
        File.Exists(modelPath) && File.Exists(tokenizerPath);

    // Raw SentencePiece id -> HF/fairseq XLM-RoBERTa id: content token raw_id (raw_id != 0)
    // shifts by fairseq_offset=1; the piece at raw_id 0 (the .model's own <unk>) maps to the
    // fairseq unk id 3. Wrapped with fairseq's fixed bos=0 / eos=2 (never produced by the raw
    // tokenizer, which doesn't share fairseq's reserved-id scheme).
    private static long[] RemapToFairseqIds(IReadOnlyList<int> rawIds)
    {
        var result = new long[rawIds.Count + 2];
        result[0] = 0L; // <s>
        for (var i = 0; i < rawIds.Count; i++)
            result[i + 1] = rawIds[i] == 0 ? 3L : rawIds[i] + 1L;
        result[^1] = 2L; // </s>
        return result;
    }

    public float[] Embed(string text)
    {
        var ids = RemapToFairseqIds(_tokenizer.EncodeToIds(text));
        var inputIds = new DenseTensor<long>(ids, new[] { 1, ids.Length });
        var attentionMask = new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), new[] { 1, ids.Length });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
        };

        // Some BERT-architecture exports (type_vocab_size=2) also declare a third input,
        // token_type_ids. Only add it when the loaded model actually asks for it, so this
        // works whether or not the real model needs it, instead of hardcoding exactly two
        // inputs and risking a "Missing Input" exception the first time this runs for real.
        if (_session.InputMetadata.ContainsKey("token_type_ids"))
        {
            var tokenTypeIds = new DenseTensor<long>(new long[ids.Length], new[] { 1, ids.Length });
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds));
        }

        using var outputs = _session.Run(inputs);
        var lastHiddenState = outputs.First(o => o.Name == "last_hidden_state").AsTensor<float>();

        // Усреднение по токенам (mean pooling), затем L2-нормализация — так задокументирована
        // стратегия пулинга для эмбеддингов multilingual-e5.
        var hidden = lastHiddenState.Dimensions[2];
        var tokens = lastHiddenState.Dimensions[1];
        var pooled = new float[hidden];
        for (var t = 0; t < tokens; t++)
            for (var h = 0; h < hidden; h++)
                pooled[h] += lastHiddenState[0, t, h];
        for (var h = 0; h < hidden; h++)
            pooled[h] /= tokens;

        var norm = (float)Math.Sqrt(pooled.Sum(v => v * v));
        if (norm > 0)
            for (var h = 0; h < hidden; h++)
                pooled[h] /= norm;

        return pooled;
    }

    public void Dispose() => _session.Dispose();
}
