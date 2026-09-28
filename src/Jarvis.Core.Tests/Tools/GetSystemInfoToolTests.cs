// src/Jarvis.Core.Tests/Tools/GetSystemInfoToolTests.cs
using Jarvis.Core.Tools;
using Jarvis.Core.Tools.OsActions;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeSystemInfoProvider : ISystemInfoProvider
{
    public SystemInfoSnapshot GetSnapshot() => new(
        BatteryPercent: 73,
        IsCharging: true,
        RamUsedGb: 8.2,
        RamTotalGb: 16,
        CpuLoadPercent: 12.5,
        FreeDiskGb: 120.4);
}

public class GetSystemInfoToolTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsSummaryWithBatteryAndRam()
    {
        var tool = new GetSystemInfoTool(new FakeSystemInfoProvider());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>(), new ToolContext());

        Assert.True(result.Success);
        Assert.Contains("73", result.Message);
        Assert.Contains("8.2", result.Message);
        Assert.Null(result.Confirmation);
    }
}
