using Jarvis.Core.Config;
using Jarvis.Core.Nlu;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class FakeEmbeddingModel : IEmbeddingModel
{
    // Возвращает почти one-hot вектор по "ключевому слову темы" во фразе,
    // чтобы косинусное сходство было детерминированным и предсказуемым в тестах.
    //
    // Фразы без узнаваемой темы получают НУЛЕВОЙ вектор, а не отдельную one-hot категорию
    // "unrelated": CosineSimilarity возвращает 0 для нулевого вектора (см. EmbeddingResolver),
    // поэтому такая фраза никогда не совпадёт ни с одним embedding-сэмплом. Раньше, когда
    // "unrelated" было полноценной one-hot категорией (topic 3), ЛЮБАЯ фраза без ключевых слов
    // получала cosine=1.0 с ЛЮБОЙ другой такой же безключевой фразой — а среди реальных
    // intent-сэмплов из config/intents (используемых PhraseFixtureTests) таких фраз десятки
    // (например, все примеры system_control), так что заведомо нерелевантные фразы вроде
    // "расскажи анекдот про кота" ложно резолвились в случайный инструмент.
    private static readonly string[] Topics = { "app", "volume", "battery" };

    public float[] Embed(string text)
    {
        var vector = new float[Topics.Length];
        var lower = text.ToLowerInvariant();
        if (lower.Contains("хром") || lower.Contains("открой") || lower.Contains("браузер")) vector[0] = 1f;
        else if (lower.Contains("звук") || lower.Contains("гром")) vector[1] = 1f;
        else if (lower.Contains("батаре")) vector[2] = 1f;
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
