using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jarvis.Core.Tools.OsActions;

public class WindowsSystemPowerActions : ISystemPowerActions
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public void Lock() => LockWorkStation();

    public void ShutdownWithDelay(int seconds) =>
        Process.Start(new ProcessStartInfo("shutdown", $"/s /t {seconds}") { CreateNoWindow = true, UseShellExecute = false });

    public void RestartWithDelay(int seconds) =>
        Process.Start(new ProcessStartInfo("shutdown", $"/r /t {seconds}") { CreateNoWindow = true, UseShellExecute = false });

    public void Sleep() => SetSuspendState(false, false, false);

    public void CancelShutdown() =>
        Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false });
}
