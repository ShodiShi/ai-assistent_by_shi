namespace Jarvis.Core.Config;

public record AppEntry
{
    public required string Name { get; init; }
    public List<string> Aliases { get; init; } = new();
    public string? Path { get; init; }
    public string? ShellCommand { get; init; }
}
