using Jarvis.Core.Tools.OsActions;

namespace Jarvis.Core.Tools;

public class SystemControlTool : ITool
{
    private readonly ISystemPowerActions _power;
    private readonly int _confirmSeconds;

    public SystemControlTool(ISystemPowerActions power, int confirmSeconds)
    {
        _power = power;
        _confirmSeconds = confirmSeconds;
    }

    public string Name => "system_control";

    public IReadOnlyDictionary<string, ArgSpec> ArgsSchema { get; } = new Dictionary<string, ArgSpec>
    {
        ["action"] = new ArgSpec(ArgType.String, Required: true, AllowedValues: new[] { "lock", "shutdown", "restart", "sleep" }),
    };

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        var action = args.GetValueOrDefault("action") as string ?? "";

        if (action == "lock")
        {
            _power.Lock();
            return Task.FromResult(new ToolResult(true, "Экран заблокирован."));
        }

        if (action is not ("shutdown" or "restart" or "sleep"))
            return Task.FromResult(new ToolResult(false, $"Неизвестное действие: {action}"));

        if (!context.IsConfirmed)
        {
            var prompt = action switch
            {
                "shutdown" => $"Выключить ноутбук через {_confirmSeconds} секунд? (да/отмена)",
                "restart" => $"Перезагрузить ноутбук через {_confirmSeconds} секунд? (да/отмена)",
                _ => "Перевести ноутбук в сон? (да/отмена)",
            };
            var confirmation = new PendingConfirmation(
                ToolName: "system_control",
                Args: new Dictionary<string, object?> { ["action"] = action },
                Prompt: prompt);
            return Task.FromResult(new ToolResult(false, prompt, confirmation));
        }

        switch (action)
        {
            case "shutdown":
                _power.ShutdownWithDelay(_confirmSeconds);
                return Task.FromResult(new ToolResult(true, $"Выключаю через {_confirmSeconds} секунд. Скажите «отмена», чтобы остановить."));
            case "restart":
                _power.RestartWithDelay(_confirmSeconds);
                return Task.FromResult(new ToolResult(true, $"Перезагружаю через {_confirmSeconds} секунд. Скажите «отмена», чтобы остановить."));
            default:
                _power.Sleep();
                return Task.FromResult(new ToolResult(true, "Ухожу в сон."));
        }
    }
}
