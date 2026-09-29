using System.Text.RegularExpressions;

namespace Jarvis.Core.Nlu;

public class RuleBasedResolver : ICommandResolver
{
    public int Level => 1;
    public bool IsAvailable => true;

    private static string Normalize(string text) =>
        Regex.Replace(text.Trim().ToLowerInvariant(), @"[!?.,;]+", "").Trim();

    private static string StripPolitenessFillers(string appName)
    {
        var normalized = appName.Trim();

        // Sort fillers by length (longest first) to match multi-word phrases before partial matches
        var sortedfFillers = PolitenessFillers.OrderByDescending(f => f.Length).ToArray();

        // Try stripping up to twice to handle any stacked fillers
        for (int attempt = 0; attempt < 2; attempt++)
        {
            foreach (var filler in sortedfFillers)
            {
                // Check if normalized name ends with this filler phrase
                if (normalized.EndsWith(" " + filler, StringComparison.InvariantCultureIgnoreCase))
                {
                    normalized = normalized.Substring(0, normalized.Length - filler.Length - 1).Trim();
                    break; // Found and stripped, try again from top
                }
                else if (normalized.Equals(filler, StringComparison.InvariantCultureIgnoreCase))
                {
                    // Edge case: name is just the filler, shouldn't happen but handle it
                    normalized = "";
                    break;
                }
            }
        }

        return normalized;
    }

    private static readonly string[] ShutdownWords = { "выключи ноут", "выключи компьютер", "выключи комп", "выключи ноутбук" };
    private static readonly string[] RestartWords = { "перезагрузи компьютер", "перезагрузи комп", "перезагрузи ноутбук", "ребут" };
    private static readonly string[] LockWords = { "заблокируй экран", "заблокируй компьютер", "заблокируй ноутбук" };
    private static readonly string[] SleepWords = { "усыпи ноутбук", "усыпи компьютер", "спящий режим", "пора спать" };
    private static readonly string[] PolitenessFillers = { "пожалуйста", "плиз", "будь добр", "будьте добры" };

    // Strips one known trailing phrase (e.g. "на экран") from a captured app name, if present.
    private static string StripTrailingPhrase(string name, string suffix)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(" " + suffix, StringComparison.InvariantCultureIgnoreCase)
            ? trimmed[..^(suffix.Length + 1)].Trim()
            : trimmed;
    }

    public Task<ResolveResult> ResolveAsync(string utterance, NluContext context)
    {
        var text = Normalize(utterance);

        var openMatch = Regex.Match(text, @"^(открой|запусти)\s+(?<name>.+)$");
        if (openMatch.Success)
            return Resolved("open_app", new() { ["name"] = StripPolitenessFillers(openMatch.Groups["name"].Value.Trim()) });

        var closeMatch = Regex.Match(text, @"^(закрой|вырубай|выруби)\s+(?<name>.+)$");
        if (closeMatch.Success)
            return Resolved("close_app", new() { ["name"] = StripPolitenessFillers(closeMatch.Groups["name"].Value.Trim()) });

        // Разговорные формулировки открытия приложения ("подними X на экран", "хочу открыть X")
        // — покрываем их на уровне 1 напрямую, а не полагаемся на уровень 2, у которого нет
        // надёжного способа отличить "открыть" от "закрыть" по одному только ключевому слову
        // приложения ("хром" встречается в обучающих фразах и open_app, и close_app).
        var liftMatch = Regex.Match(text, @"^подними\s+(?<name>.+)$");
        if (liftMatch.Success)
            return Resolved("open_app", new() { ["name"] = StripPolitenessFillers(StripTrailingPhrase(liftMatch.Groups["name"].Value, "на экран")) });

        var wantOpenMatch = Regex.Match(text, @"^хочу\s+открыть\s+(?<name>.+)$");
        if (wantOpenMatch.Success)
            return Resolved("open_app", new() { ["name"] = StripPolitenessFillers(wantOpenMatch.Groups["name"].Value.Trim()) });

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
        if (text.Contains("выключи звук") || text.Contains("без звука") || text.Contains("замьють") || text.Contains("заглуши"))
            return Resolved("volume_control", new() { ["action"] = "mute" });
        if (text.Contains("включи звук") || text.Contains("верни звук"))
            return Resolved("volume_control", new() { ["action"] = "unmute" });

        // "включи X" вне контекста звука — ещё одна разговорная форма открытия приложения.
        // Проверяется после веток управления звуком, чтобы "включи звук" не перехватывался тут.
        var turnOnMatch = Regex.Match(text, @"^включи\s+(?<name>.+)$");
        if (turnOnMatch.Success)
            return Resolved("open_app", new() { ["name"] = StripPolitenessFillers(turnOnMatch.Groups["name"].Value.Trim()) });

        if (text.Contains("батаре") || text.Contains("заряд") || text.Contains("сколько памяти") ||
            text.Contains("процессор") || text.Contains("диске") ||
            (text.Contains("информаци") && text.Contains("систем")))
            return Resolved("get_system_info", new());

        // Разговорные формулировки закрытия приложения — последний резерв перед Unresolved,
        // чтобы более специфичные ветки (system_control, volume_control) успевали сработать первыми
        // ("выключи ноут"/"выключи звук" и т.п. не должны долетать до этой ветки).
        var screenRemoveMatch = Regex.Match(text, @"^убери\s+со\s+экрана\s+(?<name>.+)$");
        if (screenRemoveMatch.Success)
            return Resolved("close_app", new() { ["name"] = StripPolitenessFillers(screenRemoveMatch.Groups["name"].Value.Trim()) });

        var turnOffMatch = Regex.Match(text, @"^выключи\s+(?<name>.+)$");
        if (turnOffMatch.Success)
            return Resolved("close_app", new() { ["name"] = StripPolitenessFillers(turnOffMatch.Groups["name"].Value.Trim()) });

        return Task.FromResult(ResolveResult.Unresolved());
    }

    private Task<ResolveResult> Resolved(string tool, Dictionary<string, object?> args) =>
        Task.FromResult(ResolveResult.For(tool, args, confidence: 1.0, level: Level));
}
