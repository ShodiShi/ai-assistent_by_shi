using Jarvis.Core.Config;
using Jarvis.Core.Nlu;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Jarvis.Core.Tests.Nlu;

public class PhraseFixtureTests
{
    private record FixtureCase
    {
        public string Phrase { get; init; } = "";
        public string? ExpectedTool { get; init; }
    }

    private record FixtureFile
    {
        public List<FixtureCase> Cases { get; init; } = new();
    }

    private static List<FixtureCase> LoadCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "test-phrases.yaml");
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        var file = deserializer.Deserialize<FixtureFile>(File.ReadAllText(path));
        return file.Cases;
    }

    // dotnet test runs xUnit out of the test project's build output directory (e.g.
    // src/Jarvis.Core.Tests/bin/Debug/net8.0), not the repo root, so a working-directory-relative
    // "config/intents" path does not resolve there. Walk up from the current working directory
    // (and, as a fallback, from the test assembly's own location) looking for the repo's marker
    // file/folder so this works regardless of where the test runner's cwd happens to be.
    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Jarvis.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate repo root (looked for Jarvis.sln walking up from " +
            $"'{Directory.GetCurrentDirectory()}' and '{AppContext.BaseDirectory}').");
    }

    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>
    {
        new() { Name = "Google Chrome", Aliases = new() { "хром" } },
        new() { Name = "Telegram", Aliases = new() { "телеграм" } },
        new() { Name = "Discord", Aliases = new() { "дискорд" } },
    }));

    [Fact]
    public async Task Fixture_HasAtLeast50Cases()
    {
        Assert.True(LoadCases().Count >= 50, $"Expected 50+ cases, found {LoadCases().Count}");
    }

    [Fact]
    public async Task Fixture_AtLeast70PercentResolveViaLevel1Or2()
    {
        var intentsDir = Path.Combine(FindRepoRoot(), "config", "intents");
        var samples = IntentCatalog.LoadFromDirectory(intentsDir);
        Assert.NotEmpty(samples);
        var resolvers = new List<ICommandResolver>
        {
            new RuleBasedResolver(),
            new EmbeddingResolver(new FakeEmbeddingModel(), samples, threshold: 0.3),
        };
        var context = BuildContext();
        var cases = LoadCases();
        var resolvedCount = 0;

        foreach (var testCase in cases)
        {
            ResolveResult? match = null;
            foreach (var resolver in resolvers.OrderBy(r => r.Level))
            {
                var result = await resolver.ResolveAsync(testCase.Phrase, context);
                if (result.Resolved) { match = result; break; }
            }

            if (testCase.ExpectedTool is null)
            {
                Assert.True(match is null, $"'{testCase.Phrase}' expected Unresolved but got '{match?.ToolName}'");
            }
            else
            {
                Assert.NotNull(match);
                Assert.Equal(testCase.ExpectedTool, match!.ToolName);
                resolvedCount++;
            }
        }

        var ratio = (double)resolvedCount / cases.Count(c => c.ExpectedTool is not null);
        Assert.True(ratio >= 0.70, $"Only {ratio:P0} of resolvable phrases matched (target >= 70%)");
    }
}
