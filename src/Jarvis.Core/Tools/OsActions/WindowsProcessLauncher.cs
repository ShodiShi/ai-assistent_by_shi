using System.Diagnostics;
using Jarvis.Core.Config;

namespace Jarvis.Core.Tools.OsActions;

public class WindowsProcessLauncher : IProcessLauncher
{
    public void Launch(AppEntry entry)
    {
        var path = Environment.ExpandEnvironmentVariables(entry.Path ?? entry.Name);
        var psi = new ProcessStartInfo(path)
        {
            Arguments = entry.ShellCommand ?? "",
            UseShellExecute = true,
        };
        Process.Start(psi);
    }
}
