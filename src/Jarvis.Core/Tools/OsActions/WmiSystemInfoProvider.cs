using System.Diagnostics;
using System.Management;

namespace Jarvis.Core.Tools.OsActions;

public class WmiSystemInfoProvider : ISystemInfoProvider
{
    public SystemInfoSnapshot GetSnapshot()
    {
        int batteryPercent = 100;
        bool isCharging = true;
        using (var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery"))
        {
            foreach (var obj in searcher.Get())
            {
                batteryPercent = Convert.ToInt32(obj["EstimatedChargeRemaining"] ?? 100);
                var status = Convert.ToInt32(obj["BatteryStatus"] ?? 2);
                isCharging = status == 2 || status == 6;
            }
        }

        double ramTotalGb = 0, ramFreeGb = 0;
        using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
        {
            foreach (var obj in searcher.Get())
            {
                ramTotalGb = Convert.ToDouble(obj["TotalVisibleMemorySize"]) / 1024 / 1024;
                ramFreeGb = Convert.ToDouble(obj["FreePhysicalMemory"]) / 1024 / 1024;
            }
        }

        double cpuLoad;
        using (var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total"))
        {
            counter.NextValue();
            Thread.Sleep(200);
            cpuLoad = counter.NextValue();
        }

        var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        double freeDiskGb = systemDrive.AvailableFreeSpace / 1024.0 / 1024 / 1024;

        return new SystemInfoSnapshot(
            batteryPercent,
            isCharging,
            Math.Round(ramTotalGb - ramFreeGb, 1),
            Math.Round(ramTotalGb, 1),
            Math.Round(cpuLoad, 1),
            Math.Round(freeDiskGb, 1));
    }
}
