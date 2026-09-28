namespace Jarvis.Core.Tools.OsActions;

public interface IVolumeController
{
    void Increase(int percent);
    void Decrease(int percent);
    void Mute();
    void Unmute();
}
