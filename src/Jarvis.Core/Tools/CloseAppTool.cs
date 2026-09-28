using Jarvis.Core.Config;
using Jarvis.Core.Tools.OsActions;

namespace Jarvis.Core.Tools;

public class CloseAppTool : ITool
{
    private readonly AppsCatalog _catalog;
    private readonly IProcessCloser _closer;

    public CloseAppTool(AppsCatalog catalog, IProcessCloser closer)
    {
        _catalog = catalog;
        _closer = closer;
    }

    public string Name => "close_app";

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        var name = args.GetValueOrDefault("name") as string ?? "";
        var entry = _catalog.FindByNameOrAlias(name);
        if (entry is null)
            return Task.FromResult(new ToolResult(false, $"Не знаю приложение «{name}»."));

        var processName = Path.GetFileNameWithoutExtension(entry.Path ?? entry.Name);

        if (context.IsConfirmed)
        {
            _closer.ForceKill(processName);
            return Task.FromResult(new ToolResult(true, $"{entry.Name} принудительно закрыт."));
        }

        if (_closer.CountRunning(processName) == 0)
            return Task.FromResult(new ToolResult(true, $"{entry.Name} и так не запущен."));

        var stillRunning = _closer.SoftClose(processName);
        if (stillRunning == 0)
            return Task.FromResult(new ToolResult(true, $"{entry.Name} закрыт."));

        var confirmation = new PendingConfirmation(
            ToolName: "close_app",
            Args: new Dictionary<string, object?> { ["name"] = name },
            Prompt: $"{entry.Name} не закрылся сам. Закрыть принудительно? (да/отмена)");
        return Task.FromResult(new ToolResult(false, confirmation.Prompt, confirmation));
    }
}
