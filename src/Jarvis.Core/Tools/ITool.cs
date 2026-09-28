namespace Jarvis.Core.Tools;

public interface ITool
{
    string Name { get; }
    Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context);
}
