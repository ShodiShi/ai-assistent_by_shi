namespace Jarvis.Core.Tools;

public record ToolResult(bool Success, string Message, PendingConfirmation? Confirmation = null);
