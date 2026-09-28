using System.Diagnostics;

namespace Jarvis.Core.Tools.OsActions;

public class WindowsProcessCloser : IProcessCloser
{
    private static string StripExtension(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    public int CountRunning(string processName) =>
        Process.GetProcessesByName(StripExtension(processName)).Length;

    public int SoftClose(string processName)
    {
        var procs = Process.GetProcessesByName(StripExtension(processName));
        foreach (var p in procs)
        {
            if (p.MainWindowHandle != IntPtr.Zero)
                p.CloseMainWindow();
        }
        Thread.Sleep(500);
        return CountRunning(processName);
    }

    public int ForceKill(string processName)
    {
        var procs = Process.GetProcessesByName(StripExtension(processName));
        foreach (var p in procs)
            p.Kill();
        return 0;
    }
}
