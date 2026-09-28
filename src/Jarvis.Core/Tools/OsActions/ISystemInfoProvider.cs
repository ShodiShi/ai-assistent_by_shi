namespace Jarvis.Core.Tools.OsActions;

public interface ISystemInfoProvider
{
    SystemInfoSnapshot GetSnapshot();
}
