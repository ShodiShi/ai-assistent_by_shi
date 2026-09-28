using NAudio.CoreAudioApi;

namespace Jarvis.Core.Tools.OsActions;

public class NAudioVolumeController : IVolumeController
{
    private MMDevice GetDefaultDevice()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public void Increase(int percent)
    {
        var device = GetDefaultDevice();
        var current = device.AudioEndpointVolume.MasterVolumeLevelScalar;
        device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(current + percent / 100f, 0f, 1f);
    }

    public void Decrease(int percent)
    {
        var device = GetDefaultDevice();
        var current = device.AudioEndpointVolume.MasterVolumeLevelScalar;
        device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(current - percent / 100f, 0f, 1f);
    }

    public void Mute() => GetDefaultDevice().AudioEndpointVolume.Mute = true;

    public void Unmute() => GetDefaultDevice().AudioEndpointVolume.Mute = false;
}
