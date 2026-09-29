namespace Jarvis.Core.Tools;

public interface ITool
{
    string Name { get; }

    // Default: no declared arguments (anything goes, nothing required) — test doubles and any
    // future trivial tool don't need to override this. Real tools with actual arguments should.
    IReadOnlyDictionary<string, ArgSpec> ArgsSchema => new Dictionary<string, ArgSpec>();

    Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context);
}
