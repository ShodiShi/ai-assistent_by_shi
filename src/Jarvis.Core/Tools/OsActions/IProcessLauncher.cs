using Jarvis.Core.Config;

namespace Jarvis.Core.Tools.OsActions;

public interface IProcessLauncher
{
    void Launch(AppEntry entry);
}
