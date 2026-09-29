namespace Jarvis.Core.Nlu;

public class EmbeddingResolver : ICommandResolver
{
    private readonly IEmbeddingModel _model;
    private readonly double _threshold;
    private readonly List<(IntentSample Sample, float[] Vector)> _index;

    public EmbeddingResolver(IEmbeddingModel model, IReadOnlyList<IntentSample> samples, double threshold)
    {
        _model = model;
        _threshold = threshold;
        // e5's card documents "passage: "/"query: " for asymmetric retrieval (find a passage
        // that answers a query) and "query: " on both sides for symmetric similarity tasks —
        // matching a user utterance against example phrases of the same kind is symmetric, and
        // empirically (OnnxEmbeddingModelIntegrationTests.RealCatalog_EmbeddingResolver, full
        // 50-phrase fixture) "query: " on both sides resolves more correctly at the same
        // false-positive rate than the "passage: " asymmetric convention.
        _index = samples.Select(s => (s, model.Embed("query: " + s.Phrase))).ToList();
    }

    public int Level => 2;
    public bool IsAvailable => _index.Count > 0;

    public Task<ResolveResult> ResolveAsync(string utterance, NluContext context)
    {
        if (!IsAvailable)
            return Task.FromResult(ResolveResult.Unresolved());

        var queryVector = _model.Embed("query: " + utterance);

        var best = _index
            .Select(entry => (entry.Sample, Score: CosineSimilarity(queryVector, entry.Vector)))
            .OrderByDescending(x => x.Score)
            .First();

        if (best.Score < _threshold)
            return Task.FromResult(ResolveResult.Unresolved());

        var args = ArgExtraction.ExtractArgs(best.Sample.Tool, utterance, context.Apps);
        return Task.FromResult(ResolveResult.For(best.Sample.Tool, args, best.Score, Level));
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA == 0 || magB == 0) return 0;
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
