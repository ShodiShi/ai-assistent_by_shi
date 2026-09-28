namespace Jarvis.Core.Tools.OsActions;

public interface ISystemPowerActions
{
    void Lock();
    void ShutdownWithDelay(int seconds);
    void RestartWithDelay(int seconds);
    void Sleep();
    void CancelShutdown();
}
