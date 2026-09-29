using Jarvis.Core.Config;
using Jarvis.Core.Nlu;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class RuleBasedResolverTests
{
    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>
    {
        new() { Name = "Google Chrome", Aliases = new() { "хром", "chrome" } }
    }));

    private readonly RuleBasedResolver _resolver = new();

    [Theory]
    [InlineData("открой хром")]
    [InlineData("Открой ХРОМ!!")]
    [InlineData("  открой   хром  ")]
    public async Task ResolveAsync_OpenApp_IsRobustToCaseAndPunctuation(string phrase)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("open_app", result.ToolName);
        Assert.Equal("хром", result.Args["name"]);
    }

    [Fact]
    public async Task ResolveAsync_CloseApp_ExtractsName()
    {
        var result = await _resolver.ResolveAsync("закрой хром", BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("close_app", result.ToolName);
        Assert.Equal("хром", result.Args["name"]);
    }

    [Theory]
    [InlineData("выключи ноут", "shutdown")]
    [InlineData("перезагрузи компьютер", "restart")]
    [InlineData("заблокируй экран", "lock")]
    [InlineData("усыпи ноутбук", "sleep")]
    public async Task ResolveAsync_SystemControl_MapsPhraseToAction(string phrase, string expectedAction)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("system_control", result.ToolName);
        Assert.Equal(expectedAction, result.Args["action"]);
    }

    [Theory]
    [InlineData("громче", "up")]
    [InlineData("сделай тише", "down")]
    [InlineData("выключи звук", "mute")]
    [InlineData("включи звук", "unmute")]
    public async Task ResolveAsync_Volume_MapsPhraseToAction(string phrase, string expectedAction)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("volume_control", result.ToolName);
        Assert.Equal(expectedAction, result.Args["action"]);
    }

    [Fact]
    public async Task ResolveAsync_VolumeWithAmount_ExtractsAmount()
    {
        var result = await _resolver.ResolveAsync("громкость на 30 процентов", BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("volume_control", result.ToolName);
        Assert.Equal(30, result.Args["amount"]);
    }

    // Regression coverage for a real bug: the "подними X" -> open_app, "включи X" -> open_app
    // and "выключи X" -> close_app catch-alls (added for conversational open/close phrasing)
    // were originally placed/scoped so they stole real volume_control phrases that happen to
    // start with the same verbs ("подними звук" is a real config/intents/volume_control.yaml
    // sample). These must keep resolving to volume_control, never open_app/close_app.
    [Theory]
    [InlineData("подними звук", "up")]
    [InlineData("подними звук погромче", "up")]
    [InlineData("подними громкость", "up")]
    [InlineData("включи громкость", "unmute")]
    [InlineData("выключи громкость", "mute")]
    public async Task ResolveAsync_VolumeVerbsSharedWithAppCatchAlls_ResolveAsVolumeControl(string phrase, string expectedAction)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("volume_control", result.ToolName);
        Assert.Equal(expectedAction, result.Args["action"]);
    }

    [Fact]
    public async Task ResolveAsync_LiftVolumeWithAmount_ExtractsAmount()
    {
        var result = await _resolver.ResolveAsync("подними громкость на 30", BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("volume_control", result.ToolName);
        Assert.Equal("up", result.Args["action"]);
        Assert.Equal(30, result.Args["amount"]);
    }

    // Same regression class for the "выключи X" -> close_app catch-all: phrases that mean
    // "shut down the computer" in other words ("пк", "систему") must still resolve as
    // system_control shutdown, not get swallowed by the generic close_app fallback.
    [Theory]
    [InlineData("выключи пк", "shutdown")]
    [InlineData("выключи систему", "shutdown")]
    public async Task ResolveAsync_ShutdownSynonymsSharedWithCloseAppCatchAll_ResolveAsSystemControl(string phrase, string expectedAction)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("system_control", result.ToolName);
        Assert.Equal(expectedAction, result.Args["action"]);
    }

    // Regression coverage for a real bug: whenever a number was present alongside a volume word,
    // the "громкость/звук + number" branch hardcoded action=up without looking at the verb, so an
    // explicit decrease verb with a number came out as the OPPOSITE direction.
    [Theory]
    [InlineData("убавь звук на 20", "down", 20)]
    [InlineData("уменьши громкость на 20", "down", 20)]
    [InlineData("убавь громкость на 30 процентов", "down", 30)]
    [InlineData("понизь громкость на 10", "down", 10)]
    [InlineData("сделай звук тише на 15", "down", 15)]
    [InlineData("прибавь звука процентов на 20", "up", 20)]
    [InlineData("громкость на 30", "up", 30)] // no directional verb at all -> "up" default
    public async Task ResolveAsync_VolumeWithAmount_TakesDirectionFromVerb(string phrase, string expectedAction, int expectedAmount)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("volume_control", result.ToolName);
        Assert.Equal(expectedAction, result.Args["action"]);
        Assert.Equal(expectedAmount, result.Args["amount"]);
    }

    // "включи звук" means "switch the sound on" (unmute) — the same action level 1 already gives
    // the bare "включи звук" above. With a number attached the verb still means "on", not
    // "raise": VolumeControlTool's "up" only moves the level and never clears the mute flag, so
    // "up 50" on a muted system would stay silent. The amount is kept in the args (the tool
    // ignores it for unmute) rather than silently dropped.
    [Fact]
    public async Task ResolveAsync_TurnOnSoundWithAmount_ResolvesAsUnmute()
    {
        var result = await _resolver.ResolveAsync("включи звук на 50", BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("volume_control", result.ToolName);
        Assert.Equal("unmute", result.Args["action"]);
        Assert.Equal(50, result.Args["amount"]);
    }

    // Regression coverage for a real bug: the "закрой|вырубай|выруби X" -> close_app regex
    // claimed ANY phrase starting with "вырубай"/"выруби", so "вырубай комп" (an example phrase
    // named in the Stage 1 spec) replied «Не знаю приложение «комп».» instead of shutting down,
    // and "выруби звук" tried to close an app called "звук". The last two phrases of each group
    // are real config/intents/*.yaml catalog samples.
    [Theory]
    [InlineData("вырубай комп")]
    [InlineData("выруби ноутбук")]
    [InlineData("выруби пк")]
    [InlineData("вырубай комп совсем")]
    public async Task ResolveAsync_CutVerbWithComputer_ResolvesAsShutdown(string phrase)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("system_control", result.ToolName);
        Assert.Equal("shutdown", result.Args["action"]);
    }

    [Theory]
    [InlineData("выруби звук")]
    [InlineData("вырубай громкость")]
    [InlineData("выруби звук на компе")] // volume object wins over the incidental "комп"
    [InlineData("выруби звук совсем")]
    public async Task ResolveAsync_CutVerbWithSound_ResolvesAsMute(string phrase)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("volume_control", result.ToolName);
        Assert.Equal("mute", result.Args["action"]);
    }

    // The guard above must not over-exclude: a real app name after "вырубай"/"выруби"/"закрой"
    // still goes to close_app, including names that merely contain the letters "пк" inside a
    // longer word ("пк" is only treated as "computer" as a whole word).
    [Theory]
    [InlineData("вырубай дискорд", "дискорд")]
    [InlineData("выруби хром", "хром")]
    [InlineData("закрой папку", "папку")]
    public async Task ResolveAsync_CutVerbWithAppName_StillResolvesAsCloseApp(string phrase, string expectedName)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("close_app", result.ToolName);
        Assert.Equal(expectedName, result.Args["name"]);
    }

    [Theory]
    [InlineData("как там батарея")]
    [InlineData("сколько памяти свободно")]
    [InlineData("покажи информацию о системе")]
    public async Task ResolveAsync_SystemInfo_Resolves(string phrase)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("get_system_info", result.ToolName);
    }

    [Fact]
    public async Task ResolveAsync_UnrecognizedPhrase_ReturnsUnresolved()
    {
        var result = await _resolver.ResolveAsync("расскажи анекдот про кота", BuildContext());

        Assert.False(result.Resolved);
    }

    [Theory]
    [InlineData("открой хром, пожалуйста")]
    [InlineData("открой хром пожалуйста")]
    [InlineData("запусти хром плиз")]
    [InlineData("открой хром будь добр")]
    [InlineData("открой хром будьте добры")]
    public async Task ResolveAsync_OpenApp_StripsPolitenessFillers(string phrase)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("open_app", result.ToolName);
        Assert.Equal("хром", result.Args["name"]);
    }

    [Theory]
    [InlineData("закрой хром, пожалуйста")]
    [InlineData("закрой хром пожалуйста")]
    [InlineData("закрой хром будь добр")]
    [InlineData("закрой хром будьте добры")]
    public async Task ResolveAsync_CloseApp_StripsPolitenessFillers(string phrase)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("close_app", result.ToolName);
        Assert.Equal("хром", result.Args["name"]);
    }
}
