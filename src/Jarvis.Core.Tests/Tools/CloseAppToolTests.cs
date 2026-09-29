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

    // explorer.exe is the Windows shell itself (desktop + taskbar): soft-closing its main window
    // or force-killing it takes the whole desktop down. Hard refusal — "да" can't override it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_Explorer_RefusesWithoutTouchingProcesses(bool confirmed)
    {
        var catalog = new AppsCatalog(new List<AppEntry>
        {
            new() { Name = "Проводник", Aliases = new() { "проводник", "explorer" }, Path = "explorer.exe" }
        });
        var fake = new FakeProcessCloser { RunningCount = 1 };
        var tool = new CloseAppTool(catalog, fake);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["name"] = "проводник" },
            new ToolContext(IsConfirmed: confirmed));

        Assert.False(result.Success);
        Assert.Null(result.Confirmation);
        Assert.Contains("оболочка Windows", result.Message);
        Assert.Equal(0, fake.SoftClosedCount);
        Assert.False(fake.ForceKillCalled);
    }

    // The guard is on the resolved process name, not the catalog entry's display name, so it
    // also holds for a full path or an explicit process_name override, in any letter case.
    [Theory]
    [InlineData(@"C:\Windows\EXPLORER.EXE", null)]
    [InlineData("something-else.exe", "Explorer")]
    [InlineData("something-else.exe", "explorer.exe")]
    public async Task ExecuteAsync_ExplorerByPathOrProcessName_Refuses(string path, string? processName)
    {
        var catalog = new AppsCatalog(new List<AppEntry>
        {
            new() { Name = "Файлы", Aliases = new() { "файлы" }, Path = path, ProcessName = processName }
        });
        var fake = new FakeProcessCloser { RunningCount = 1 };
        var tool = new CloseAppTool(catalog, fake);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["name"] = "файлы" },
            new ToolContext(IsConfirmed: true));

        Assert.False(result.Success);
        Assert.Equal(0, fake.SoftClosedCount);
        Assert.False(fake.ForceKillCalled);
    }

    // Discord's configured path is its launcher (Update.exe), which exits after starting the
    // real "Discord" process — so the process to close must come from ProcessName, not Path.
    [Fact]
    public async Task ExecuteAsync_ProcessNameOverride_IsUsedInsteadOfPath()
    {
        var catalog = new AppsCatalog(new List<AppEntry>
        {
            new() { Name = "Discord", Aliases = new() { "дискорд" }, Path = @"%LOCALAPPDATA%\Discord\Update.exe", ProcessName = "Discord" }
        });
        var fake = new FakeProcessCloser { RunningCount = 0 };
        var tool = new CloseAppTool(catalog, fake);

        await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "дискорд" }, new ToolContext());

        Assert.Equal("Discord", fake.LastProcessName);
    }

    [Fact]
    public async Task ExecuteAsync_NoProcessName_FallsBackToPathFileName()
    {
        var fake = new FakeProcessCloser { RunningCount = 0 };
        var tool = new CloseAppTool(BuildCatalog(), fake);

        await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "хром" }, new ToolContext());

        Assert.Equal("chrome", fake.LastProcessName);
    }
}

// Runs CloseAppTool against the repo's real config/apps.yaml, so a regression in the config file
// itself (not just in the tool logic) is caught.
public class CloseAppToolRealCatalogTests
{
    private static AppsCatalog LoadRealCatalog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Jarvis.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return ConfigLoader.LoadAppsCatalog(Path.Combine(dir!.FullName, "config", "apps.yaml"));
    }

    [Fact]
    public async Task Discord_ClosesTheDiscordProcess_NotItsUpdateLauncher()
    {
        var fake = new FakeProcessCloser { RunningCount = 0 };
        var tool = new CloseAppTool(LoadRealCatalog(), fake);

        await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "дискорд" }, new ToolContext());

        Assert.Equal("Discord", fake.LastProcessName);
    }

    [Fact]
    public async Task Provodnik_IsRefused()
    {
        var fake = new FakeProcessCloser { RunningCount = 1 };
        var tool = new CloseAppTool(LoadRealCatalog(), fake);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["name"] = "проводник" },
            new ToolContext(IsConfirmed: true));

        Assert.False(result.Success);
        Assert.Equal(0, fake.SoftClosedCount);
        Assert.False(fake.ForceKillCalled);
    }
}
