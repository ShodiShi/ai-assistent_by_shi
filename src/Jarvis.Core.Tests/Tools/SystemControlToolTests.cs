using Jarvis.Core.Tools;
using Jarvis.Core.Tools.OsActions;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeSystemPowerActions : ISystemPowerActions
{
    public bool LockCalled;
    public int? ShutdownDelay;
    public int? RestartDelay;
    public bool SleepCalled;
    public bool CancelCalled;

    public void Lock() => LockCalled = true;
    public void ShutdownWithDelay(int seconds) => ShutdownDelay = seconds;
    public void RestartWithDelay(int seconds) => RestartDelay = seconds;
    public void Sleep() => SleepCalled = true;
    public void CancelShutdown() => CancelCalled = true;
}

public class SystemControlToolTests
{
    [Fact]
    public async Task ExecuteAsync_Lock_ExecutesImmediatelyWithoutConfirmation()
    {
        var fake = new FakeSystemPowerActions();
        var tool = new SystemControlTool(fake, confirmSeconds: 20);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "lock" }, new ToolContext());

        Assert.True(fake.LockCalled);
        Assert.True(result.Success);
        Assert.Null(result.Confirmation);
    }

    [Fact]
    public async Task ExecuteAsync_ShutdownNotConfirmed_AsksAndDoesNotShutdown()
    {
        var fake = new FakeSystemPowerActions();
        var tool = new SystemControlTool(fake, confirmSeconds: 20);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "shutdown" }, new ToolContext());

        Assert.Null(fake.ShutdownDelay);
        Assert.NotNull(result.Confirmation);
        Assert.Equal("system_control", result.Confirmation!.ToolName);
    }

    [Fact]
    public async Task ExecuteAsync_ShutdownConfirmed_CallsShutdownWithConfiguredDelay()
    {
        var fake = new FakeSystemPowerActions();
        var tool = new SystemControlTool(fake, confirmSeconds: 20);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "shutdown" },
            new ToolContext(IsConfirmed: true));

        Assert.Equal(20, fake.ShutdownDelay);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_RestartNotConfirmed_AsksAndDoesNotRestart()
    {
        var fake = new FakeSystemPowerActions();
        var tool = new SystemControlTool(fake, confirmSeconds: 20);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "restart" }, new ToolContext());

        Assert.Null(fake.RestartDelay);
        Assert.Null(fake.ShutdownDelay);
        Assert.NotNull(result.Confirmation);
        Assert.Equal("system_control", result.Confirmation!.ToolName);
        Assert.Equal("restart", result.Confirmation.Args["action"]);
    }

    [Fact]
    public async Task ExecuteAsync_RestartConfirmed_CallsRestartWithConfiguredDelay()
    {
        var fake = new FakeSystemPowerActions();
        var tool = new SystemControlTool(fake, confirmSeconds: 45);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "restart" },
            new ToolContext(IsConfirmed: true));

        Assert.Equal(45, fake.RestartDelay);
        Assert.Null(fake.ShutdownDelay);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_SleepNotConfirmed_AsksAndDoesNotSleep()
    {
        var fake = new FakeSystemPowerActions();
        var tool = new SystemControlTool(fake, confirmSeconds: 20);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "sleep" }, new ToolContext());

        Assert.False(fake.SleepCalled);
        Assert.NotNull(result.Confirmation);
    }
}
