using Jarvis.Core.Config;
using Jarvis.Core.Nlu;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class FakeEmbeddingModel : IEmbeddingModel
{
    // Возвращает почти one-hot вектор по "ключевому слову темы" во фразе,
    // чтобы косинусное сходство было детерминированным и предсказуемым в тестах.
    private static readonly string[] Topics = { "app", "volume", "battery", "unrelated" };

    public float[] Embed(string text)
    {
        var vector = new float[Topics.Length];
        var lower = text.ToLowerInvariant();
        if (lower.Contains("хром") || lower.Contains("открой") || lower.Contains("браузер")) vector[0] = 1f;
        else if (lower.Contains("звук") || lower.Contains("гром")) vector[1] = 1f;
        else if (lower.Contains("батаре")) vector[2] = 1f;
        else vector[3] = 1f;
        return vector;
    }
}

public class EmbeddingResolverTests
{
    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>()));

    private static IReadOnlyList<IntentSample> Samples() => new List<IntentSample>
    {
        new("open_app", "открой хром"),
        new("volume_control", "сделай погромче"),
        new("get_system_info", "глянь что там с батарейкой"),
    };

    [Fact]
    public void IsAvailable_IsTrue_WhenConstructedWithSamples()
    {
        var resolver = new EmbeddingResolver(new FakeEmbeddingModel(), Samples(), threshold: 0.5);
        Assert.True(resolver.IsAvailable);
    }

    [Fact]
    public async Task ResolveAsync_SimilarPhrase_MatchesClosestIntent()
    {
        var resolver = new EmbeddingResolver(new FakeEmbeddingModel(), Samples(), threshold: 0.5);

        var result = await resolver.ResolveAsync("подними звук чуть-чуть", BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("volume_control", result.ToolName);
        Assert.Equal(2, result.Level);
    }

    [Fact]
    public async Task ResolveAsync_BelowThreshold_ReturnsUnresolved()
    {
        var resolver = new EmbeddingResolver(new FakeEmbeddingModel(), Samples(), threshold: 1.5);

        var result = await resolver.ResolveAsync("подними звук чуть-чуть", BuildContext());

        Assert.False(result.Resolved);
    }

    [Fact]
    public async Task ResolveAsync_UnrelatedPhrase_ReturnsUnresolved()
    {
        var resolver = new EmbeddingResolver(new FakeEmbeddingModel(), Samples(), threshold: 0.5);

        var result = await resolver.ResolveAsync("расскажи анекдот", BuildContext());

        Assert.False(result.Resolved);
    }
}
