namespace Jarvis.Core.Tools.OsActions;

public interface IProcessCloser
{
    int CountRunning(string processName);
    int SoftClose(string processName);
    int ForceKill(string processName);
}
