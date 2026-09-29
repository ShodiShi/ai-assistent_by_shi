using Jarvis.Core.Config;
using Jarvis.Core.Nlu;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class ArgExtractionTests
{
    // Mirrors config/apps.yaml so tests exercise the same aliases the real catalog would have.
    private static AppsCatalog BuildCatalog() => new(new List<AppEntry>
    {
        new() { Name = "Google Chrome", Aliases = new() { "хром", "гугл хром", "chrome", "браузер" } },
        new() { Name = "Telegram", Aliases = new() { "телеграм", "телега", "telegram" } },
        new() { Name = "Discord", Aliases = new() { "дискорд", "discord" } },
        new() { Name = "Visual Studio Code", Aliases = new() { "вс код", "vs code", "код", "vscode" } },
        new() { Name = "Проводник", Aliases = new() { "проводник", "файлы", "explorer" } },
        new() { Name = "Блокнот", Aliases = new() { "блокнот", "notepad" } },
    });

    // One case per phrase in config/intents/system_control.yaml, so the keyword logic is
    // checked against every real catalog phrase (the earlier ternary chain silently mis-typed
    // three of these before this test file existed).
    [Theory]
    [InlineData("выключи ноут", "shutdown")]
    [InlineData("вырубай комп совсем", "shutdown")]
    [InlineData("пора спать выключай ноутбук", "shutdown")]
    [InlineData("перезапусти систему", "restart")]
    [InlineData("ребутни комп", "restart")]
    [InlineData("заблокируй ноутбук", "lock")]
    [InlineData("поставь на паузу и заблокируй", "lock")]
    [InlineData("отправь комп спать", "sleep")]
    [InlineData("усыпи систему", "sleep")]
    [InlineData("выключи компьютер полностью", "shutdown")]
    public void ExtractArgs_SystemControl_MatchesEachCatalogPhrase(string phrase, string expectedAction)
    {
        var args = ArgExtraction.ExtractArgs("system_control", phrase, BuildCatalog());

        Assert.Equal(expectedAction, args["action"]);
    }

    [Fact]
    public void ExtractArgs_OpenApp_ResolvesNameViaAppsCatalog_ForAliasNotInOldHardcodedList()
    {
        // "файлы" is a real config/apps.yaml alias for Проводник; it was never in the old
        // hardcoded regex list, so this only passes once catalog lookup replaces it.
        var args = ArgExtraction.ExtractArgs("open_app", "открой файлы", BuildCatalog());

        Assert.Equal("файлы", args["name"]);
    }

    [Fact]
    public void ExtractArgs_CloseApp_ResolvesNameViaAppsCatalog_ForAliasNotInOldHardcodedList()
    {
        // "discord" (Latin spelling) is a real config/apps.yaml alias; also absent from the
        // old hardcoded regex list.
        var args = ArgExtraction.ExtractArgs("close_app", "закрой discord", BuildCatalog());

        Assert.Equal("discord", args["name"]);
    }

    [Fact]
    public void ExtractArgs_OpenApp_PrefersTwoWordAliasOverPartialOneWordMatch()
    {
        var args = ArgExtraction.ExtractArgs("open_app", "открой гугл хром", BuildCatalog());

        Assert.Equal("гугл хром", args["name"]);
    }

    [Fact]
    public void ExtractArgs_CloseApp_MatchesInflectedFormViaAliasPrefix()
    {
        // config/intents/close_app.yaml's own phrase: "хрома" is the genitive inflection of
        // the alias "хром" (not an exact match), and previously dropped the name entirely.
        var args = ArgExtraction.ExtractArgs("close_app", "убей процесс хрома", BuildCatalog());

        Assert.Equal("хром", args["name"]);
    }

    [Fact]
    public void ExtractArgs_CloseApp_MatchesAnotherInflectedFormViaAliasPrefix()
    {
        // "телеграма" is the genitive inflection of the alias "телеграм".
        var args = ArgExtraction.ExtractArgs("close_app", "закрой телеграма", BuildCatalog());

        Assert.Equal("телеграм", args["name"]);
    }

    // One case per phrase in config/intents/volume_control.yaml, same idea as the
    // system_control test above: level 2 extracts the action from the utterance with the same
    // verb rules as level 1, so every real catalog phrase must come out in the right direction.
    [Theory]
    [InlineData("сделай погромче", "up")]
    [InlineData("прибавь звука процентов на 20", "up")]
    [InlineData("убавь громкость", "down")]
    [InlineData("потише пожалуйста", "down")]
    [InlineData("заглуши звук", "mute")]
    [InlineData("выруби звук совсем", "mute")]
    [InlineData("верни звук обратно", "unmute")]
    [InlineData("сделай звук тише", "down")]
    [InlineData("громкость на максимум", "up")]
    [InlineData("подними звук", "up")]
    public void ExtractArgs_VolumeControl_MatchesEachCatalogPhrase(string phrase, string expectedAction)
    {
        var args = ArgExtraction.ExtractArgs("volume_control", phrase, BuildCatalog());

        Assert.Equal(expectedAction, args["action"]);
    }

    // Regression coverage for a real bug: a decrease/mute verb that the old if-chain didn't list
    // fell through to the "up" default, so level 2 turned the volume the opposite way.
    [Theory]
    [InlineData("убавь звук на 20", "down", 20)]
    [InlineData("уменьши громкость на 20", "down", 20)]
    [InlineData("убавь громкость на 30 процентов", "down", 30)]
    public void ExtractArgs_VolumeControl_DecreaseVerbWithAmount_IsDown(string phrase, string expectedAction, int expectedAmount)
    {
        var args = ArgExtraction.ExtractArgs("volume_control", phrase, BuildCatalog());

        Assert.Equal(expectedAction, args["action"]);
        Assert.Equal(expectedAmount, args["amount"]);
    }

    [Theory]
    [InlineData("выключи-ка звук")]
    [InlineData("выключи звук")]
    [InlineData("вырубай громкость")]
    public void ExtractArgs_VolumeControl_TurnOffVerb_IsMuteNotUp(string phrase)
    {
        var args = ArgExtraction.ExtractArgs("volume_control", phrase, BuildCatalog());

        Assert.Equal("mute", args["action"]);
    }

    [Fact]
    public void ExtractArgs_VolumeControl_UnmutePhrase_MatchesLevel1Behavior()
    {
        // RuleBasedResolver (level 1) resolves "включи звук" to action "unmute" — level 2
        // must extract the same action for the same phrase per "same rules as level 1".
        var args = ArgExtraction.ExtractArgs("volume_control", "включи звук", BuildCatalog());

        Assert.Equal("unmute", args["action"]);
    }
}
