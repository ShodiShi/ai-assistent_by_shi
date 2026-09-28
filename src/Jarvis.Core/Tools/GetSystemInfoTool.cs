using System.Globalization;
using Jarvis.Core.Tools.OsActions;

namespace Jarvis.Core.Tools;

public class GetSystemInfoTool : ITool
{
    private readonly ISystemInfoProvider _provider;

    public GetSystemInfoTool(ISystemInfoProvider provider) => _provider = provider;

    public string Name => "get_system_info";

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        var s = _provider.GetSnapshot();
        var charging = s.IsCharging ? "заряжается" : "от батареи";
        var message = $"Батарея {s.BatteryPercent}% ({charging}), ОЗУ {s.RamUsedGb.ToString(CultureInfo.InvariantCulture)} из {s.RamTotalGb.ToString(CultureInfo.InvariantCulture)} ГБ, " +
                      $"CPU {s.CpuLoadPercent.ToString(CultureInfo.InvariantCulture)}%, свободно на диске {s.FreeDiskGb.ToString(CultureInfo.InvariantCulture)} ГБ.";
        return Task.FromResult(new ToolResult(true, message));
    }
}
