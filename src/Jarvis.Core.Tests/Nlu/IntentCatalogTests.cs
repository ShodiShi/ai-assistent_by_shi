using Jarvis.Core.Nlu;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class IntentCatalogTests
{
    [Fact]
    public void LoadFromDirectory_ReadsAllYamlFilesAsSamples()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "open_app.yaml"), """
        tool: open_app
        phrases:
          - открой хром
          - запусти браузер
        """);
        File.WriteAllText(Path.Combine(dir, "volume_control.yaml"), """
        tool: volume_control
        phrases:
          - сделай погромче
        """);

        var samples = IntentCatalog.LoadFromDirectory(dir);

        Assert.Equal(3, samples.Count);
        Assert.Contains(samples, s => s.Tool == "open_app" && s.Phrase == "открой хром");
        Assert.Contains(samples, s => s.Tool == "volume_control" && s.Phrase == "сделай погромче");
    }
}
