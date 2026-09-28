namespace Jarvis.Core.Tools;

public record PendingConfirmation(
    string ToolName,
    IReadOnlyDictionary<string, object?> Args,
    string Prompt);
