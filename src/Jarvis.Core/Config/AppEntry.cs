namespace Jarvis.Core.Config;

public record AppEntry
{
    public required string Name { get; init; }
    public List<string> Aliases { get; init; } = new();
    public string? Path { get; init; }
    public string? ShellCommand { get; init; }

    // Name of the running process close_app looks for (YAML: process_name). Optional: when
    // unset it's derived from Path's file name. Needed when Path is a launcher that exits after
    // starting the real app (Discord's Update.exe → running process "Discord").
    public string? ProcessName { get; init; }
}
