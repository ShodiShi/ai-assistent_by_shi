namespace Jarvis.Core.Logging;

public record CommandLogEntry(
    DateTimeOffset Time,
    string Phrase,
    int Level,
    string? ToolName,
    string Result);
