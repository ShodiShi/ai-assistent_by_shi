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
