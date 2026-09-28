using System.Text.RegularExpressions;

namespace Jarvis.Core.Nlu;

public class RuleBasedResolver : ICommandResolver
{
    public int Level => 1;
    public bool IsAvailable => true;

    private static string Normalize(string text) =>
        Regex.Replace(text.Trim().ToLowerInvariant(), @"[!?.,;]+", "").Trim();

    private static readonly string[] ShutdownWords = { "выключи ноут", "выключи компьютер", "выключи комп", "выключи ноутбук" };
    private static readonly string[] RestartWords = { "перезагрузи компьютер", "перезагрузи комп", "перезагрузи ноутбук", "ребут" };
    private static readonly string[] LockWords = { "заблокируй экран", "заблокируй компьютер", "заблокируй ноутбук" };
    private static readonly string[] SleepWords = { "усыпи ноутбук", "усыпи компьютер", "спящий режим" };

    public Task<ResolveResult> ResolveAsync(string utterance, NluContext context)
    {
        var text = Normalize(utterance);

        var openMatch = Regex.Match(text, @"^(открой|запусти)\s+(?<name>.+)$");
        if (openMatch.Success)
            return Resolved("open_app", new() { ["name"] = openMatch.Groups["name"].Value.Trim() });

        var closeMatch = Regex.Match(text, @"^(закрой|вырубай|выруби)\s+(?<name>.+)$");
        if (closeMatch.Success)
            return Resolved("close_app", new() { ["name"] = closeMatch.Groups["name"].Value.Trim() });

        if (ShutdownWords.Any(w => text.Contains(w)))
            return Resolved("system_control", new() { ["action"] = "shutdown" });
        if (RestartWords.Any(w => text.Contains(w)))
            return Resolved("system_control", new() { ["action"] = "restart" });
        if (LockWords.Any(w => text.Contains(w)))
            return Resolved("system_control", new() { ["action"] = "lock" });
        if (SleepWords.Any(w => text.Contains(w)))
            return Resolved("system_control", new() { ["action"] = "sleep" });

        if (RussianNumberParser.TryExtractPercent(text, out var percent) &&
            (text.Contains("громк") || text.Contains("звук")))
        {
            return Resolved("volume_control", new() { ["action"] = "up", ["amount"] = percent });
        }

        if (text.Contains("громче") || text.Contains("прибавь звук") || text.Contains("прибавь громк"))
            return Resolved("volume_control", new() { ["action"] = "up" });
        if (text.Contains("тише") || text.Contains("убавь звук") || text.Contains("убавь громк"))
            return Resolved("volume_control", new() { ["action"] = "down" });
        if (text.Contains("выключи звук") || text.Contains("без звука") || text.Contains("замьють"))
            return Resolved("volume_control", new() { ["action"] = "mute" });
        if (text.Contains("включи звук"))
            return Resolved("volume_control", new() { ["action"] = "unmute" });

        if (text.Contains("батаре") || text.Contains("сколько памяти") || text.Contains("информаци") && text.Contains("систем"))
            return Resolved("get_system_info", new());

        return Task.FromResult(ResolveResult.Unresolved());
    }

    private Task<ResolveResult> Resolved(string tool, Dictionary<string, object?> args) =>
        Task.FromResult(ResolveResult.For(tool, args, confidence: 1.0, level: Level));
}
