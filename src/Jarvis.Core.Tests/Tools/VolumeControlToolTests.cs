// src/Jarvis.Core.Tests/Tools/VolumeControlToolTests.cs
using Jarvis.Core.Tools;
using Jarvis.Core.Tools.OsActions;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeVolumeController : IVolumeController
{
    public int IncreaseCalledWith = -1;
    public int DecreaseCalledWith = -1;
    public bool MuteCalled;
    public bool UnmuteCalled;

    public void Increase(int percent) => IncreaseCalledWith = percent;
    public void Decrease(int percent) => DecreaseCalledWith = percent;
    public void Mute() => MuteCalled = true;
    public void Unmute() => UnmuteCalled = true;
}

public class VolumeControlToolTests
{
    [Theory]
    [InlineData("up", 10)]
    [InlineData("down", 10)]
    public async Task ExecuteAsync_UsesDefaultStepWhenAmountMissing(string action, int expectedStep)
    {
        var fake = new FakeVolumeController();
        var tool = new VolumeControlTool(fake, defaultStepPercent: 10);

        await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = action }, new ToolContext());

        if (action == "up") Assert.Equal(expectedStep, fake.IncreaseCalledWith);
        else Assert.Equal(expectedStep, fake.DecreaseCalledWith);
    }

    [Fact]
    public async Task ExecuteAsync_Mute_CallsMute()
    {
        var fake = new FakeVolumeController();
        var tool = new VolumeControlTool(fake, defaultStepPercent: 10);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "mute" }, new ToolContext());

        Assert.True(fake.MuteCalled);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_UpWithExplicitAmount_UsesAmount()
    {
        var fake = new FakeVolumeController();
        var tool = new VolumeControlTool(fake, defaultStepPercent: 10);

        await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "up", ["amount"] = 25 }, new ToolContext());

        Assert.Equal(25, fake.IncreaseCalledWith);
    }
}
