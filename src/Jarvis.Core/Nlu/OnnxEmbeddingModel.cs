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
        _tokenizer = SentencePieceTokenizer.Create(tokenizerStream);
    }

    public static bool FilesExist(string modelPath, string tokenizerPath) =>
        File.Exists(modelPath) && File.Exists(tokenizerPath);

    public float[] Embed(string text)
    {
        var ids = _tokenizer.EncodeToIds(text).Select(i => (long)i).ToArray();
        var inputIds = new DenseTensor<long>(ids, new[] { 1, ids.Length });
        var attentionMask = new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), new[] { 1, ids.Length });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
        };

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
