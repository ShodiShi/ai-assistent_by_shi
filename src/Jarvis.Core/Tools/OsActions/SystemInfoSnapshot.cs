namespace Jarvis.Core.Tools.OsActions;

public record SystemInfoSnapshot(
    int BatteryPercent,
    bool IsCharging,
    double RamUsedGb,
    double RamTotalGb,
    double CpuLoadPercent,
    double FreeDiskGb);
