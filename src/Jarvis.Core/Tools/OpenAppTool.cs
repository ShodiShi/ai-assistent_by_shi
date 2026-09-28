using Jarvis.Core.Config;
using Jarvis.Core.Tools.OsActions;

namespace Jarvis.Core.Tools;

public class OpenAppTool : ITool
{
    private readonly AppsCatalog _catalog;
    private readonly IProcessLauncher _launcher;

    public OpenAppTool(AppsCatalog catalog, IProcessLauncher launcher)
    {
        _catalog = catalog;
        _launcher = launcher;
    }

    public string Name => "open_app";

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        var name = args.GetValueOrDefault("name") as string ?? "";
        var entry = _catalog.FindByNameOrAlias(name);
        if (entry is null)
            return Task.FromResult(new ToolResult(false, $"Не знаю приложение «{name}». Уточните название."));

        _launcher.Launch(entry);
        return Task.FromResult(new ToolResult(true, $"Открываю {entry.Name}."));
    }
}
