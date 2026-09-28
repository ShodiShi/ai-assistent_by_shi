using Jarvis.Core.Tools.OsActions;

namespace Jarvis.Core.Tools;

public class VolumeControlTool : ITool
{
    private readonly IVolumeController _controller;
    private readonly int _defaultStepPercent;

    public VolumeControlTool(IVolumeController controller, int defaultStepPercent)
    {
        _controller = controller;
        _defaultStepPercent = defaultStepPercent;
    }

    public string Name => "volume_control";

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        var action = args.GetValueOrDefault("action") as string ?? "";
        var amount = args.TryGetValue("amount", out var a) && a is int amt ? amt : _defaultStepPercent;

        switch (action)
        {
            case "up":
                _controller.Increase(amount);
                return Task.FromResult(new ToolResult(true, $"Громкость увеличена на {amount}%."));
            case "down":
                _controller.Decrease(amount);
                return Task.FromResult(new ToolResult(true, $"Громкость уменьшена на {amount}%."));
            case "mute":
                _controller.Mute();
                return Task.FromResult(new ToolResult(true, "Звук выключен."));
            case "unmute":
                _controller.Unmute();
                return Task.FromResult(new ToolResult(true, "Звук включён."));
            default:
                return Task.FromResult(new ToolResult(false, $"Неизвестное действие громкости: {action}"));
        }
    }
}
