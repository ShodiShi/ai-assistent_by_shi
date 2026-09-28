using Jarvis.Core.Config;
using Jarvis.Core.Tools;
using Jarvis.Core.Tools.OsActions;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeProcessCloser : IProcessCloser
{
    public int RunningCount;
    public int SoftClosedCount;
    public bool ForceKillCalled;
    public string? LastProcessName;

    public int CountRunning(string processName) { LastProcessName = processName; return RunningCount; }
    public int SoftClose(string processName) { SoftClosedCount++; return RunningCount; }
    public int ForceKill(string processName) { ForceKillCalled = true; return 0; }
}

public class CloseAppToolTests
{
    private static AppsCatalog BuildCatalog() => new(new List<AppEntry>
    {
        new() { Name = "Google Chrome", Aliases = new() { "хром" }, Path = "chrome.exe" }
    });

    [Fact]
    public async Task ExecuteAsync_NoRunningProcess_ReturnsPlainResultNoConfirmation()
    {
        var fake = new FakeProcessCloser { RunningCount = 0 };
        var tool = new CloseAppTool(BuildCatalog(), fake);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "хром" }, new ToolContext());

        Assert.True(result.Success);
        Assert.Null(result.Confirmation);
        Assert.Contains("не запущен", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessStillRunningAfterSoftClose_AsksForForceConfirmation()
    {
        var fake = new FakeProcessCloser { RunningCount = 1 };
        var tool = new CloseAppTool(BuildCatalog(), fake);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "хром" }, new ToolContext());

        Assert.Equal(1, fake.SoftClosedCount);
        Assert.NotNull(result.Confirmation);
        Assert.Equal("close_app", result.Confirmation!.ToolName);
        Assert.False(fake.ForceKillCalled);
    }

    [Fact]
    public async Task ExecuteAsync_Confirmed_ForceKillsWithoutSoftCloseAttempt()
    {
        var fake = new FakeProcessCloser { RunningCount = 1 };
        var tool = new CloseAppTool(BuildCatalog(), fake);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["name"] = "хром" },
            new ToolContext(IsConfirmed: true));

        Assert.True(fake.ForceKillCalled);
        Assert.Equal(0, fake.SoftClosedCount);
        Assert.True(result.Success);
    }
}
