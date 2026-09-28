namespace Jarvis.Core.Nlu;

public record ResolveResult(
    bool Resolved,
    string? ToolName,
    IReadOnlyDictionary<string, object?> Args,
    double Confidence,
    int Level)
{
    public static ResolveResult Unresolved() =>
        new(false, null, new Dictionary<string, object?>(), 0, 0);

    public static ResolveResult For(string toolName, IReadOnlyDictionary<string, object?> args, double confidence, int level) =>
        new(true, toolName, args, confidence, level);
}
