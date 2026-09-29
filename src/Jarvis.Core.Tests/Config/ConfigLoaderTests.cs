using Jarvis.Core.Config;
using Xunit;

namespace Jarvis.Core.Tests.Config;

public class ConfigLoaderTests
{
    [Fact]
    public void LoadAppConfig_ParsesKnownFields()
    {
        var yaml = """
        language: ru
        embedding_threshold: 0.72
        volume_step_percent: 10
        shutdown_confirm_seconds: 20
        log_retention_days: 14
        embedding_model_path: models/e5-small-int8.onnx
        embedding_tokenizer_path: models/sentencepiece.bpe.model
        intents_directory: config/intents
        """;
        var path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);

        var config = ConfigLoader.LoadAppConfig(path);

        Assert.Equal("ru", config.Language);
        Assert.Equal(0.72, config.EmbeddingThreshold);
        Assert.Equal(10, config.VolumeStepPercent);
        Assert.Equal(20, config.ShutdownConfirmSeconds);
        Assert.Equal(14, config.LogRetentionDays);
    }

    [Fact]
    public void LoadAppsCatalog_FindsEntryByAlias()
    {
        var yaml = """
        apps:
          - name: Google Chrome
            aliases: [хром, гугл хром, chrome]
            path: C:\Program Files\Google\Chrome\Application\chrome.exe
        """;
        var path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);

        var catalog = ConfigLoader.LoadAppsCatalog(path);
        var found = catalog.FindByNameOrAlias("хром");

        Assert.NotNull(found);
        Assert.Equal("Google Chrome", found!.Name);
    }

    [Fact]
    public void LoadAppsCatalog_ParsesOptionalProcessName()
    {
        var yaml = """
        apps:
          - name: Discord
            aliases: [дискорд]
            path: "%LOCALAPPDATA%\\Discord\\Update.exe"
            process_name: Discord
          - name: Блокнот
            aliases: [блокнот]
            path: notepad.exe
        """;
        var path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);

        var catalog = ConfigLoader.LoadAppsCatalog(path);

        Assert.Equal("Discord", catalog.FindByNameOrAlias("дискорд")!.ProcessName);
        Assert.Null(catalog.FindByNameOrAlias("блокнот")!.ProcessName);
    }

    [Fact]
    public void LoadAppsCatalog_UnknownAlias_ReturnsNull()
    {
        var yaml = "apps: []";
        var path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);

        var catalog = ConfigLoader.LoadAppsCatalog(path);

        Assert.Null(catalog.FindByNameOrAlias("несуществующее"));
    }
}
