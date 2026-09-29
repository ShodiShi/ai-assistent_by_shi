using Jarvis.Core.Config;

namespace Jarvis.Core.Nlu;

public static class ArgExtraction
{
    public static Dictionary<string, object?> ExtractArgs(string tool, string utterance, AppsCatalog catalog)
    {
        var args = new Dictionary<string, object?>();
        var text = utterance.ToLowerInvariant();

        switch (tool)
        {
            case "open_app":
            case "close_app":
            {
                var name = FindAppNameFromCatalog(text, catalog);
                if (name != null) args["name"] = name;
                break;
            }
            case "volume_control":
            {
                if (RussianNumberParser.TryExtractPercent(text, out var percent))
                    args["amount"] = percent;

                args["action"] = DetectVolumeAction(text);
                break;
            }
            case "system_control":
            {
                string action;
                if (text.Contains("блок"))
                    action = "lock";
                else if (text.Contains("перезапус") || text.Contains("перезагру") || text.Contains("ребут"))
                    action = "restart";
                else if (text.Contains("усып") || text.Contains("спящ") || (text.Contains("спать") && !text.Contains("выключ")))
                    action = "sleep";
                else
                    action = "shutdown";
                args["action"] = action;
                break;
            }
        }
        return args;
    }

    // Verb stems deciding the direction of a volume command, matched as substrings of the
    // lowercased utterance. Decrease and mute are checked BEFORE increase/unmute and before the
    // "up" default: an explicit "убавь"/"уменьши"/"выключи" must never come out as "up" just
    // because it wasn't listed (the old chain turned "уменьши громкость на 20" and
    // "выключи-ка звук" into "up").
    private static readonly string[] VolumeDecreaseStems = { "убав", "уменьш", "пониз", "сниз", "тиш", "меньше" };
    private static readonly string[] VolumeMuteStems = { "выключ", "выруб", "глуш", "без звука", "замьют" };
    private static readonly string[] VolumeIncreaseStems = { "прибав", "увелич", "громче", "подним", "больше" };
    // Same phrases RuleBasedResolver's own unmute branch recognizes.
    private static readonly string[] VolumeUnmuteStems = { "включи звук", "верни звук", "включи громкость" };

    // Shared by level 2 (ExtractArgs above) and level 1 (RuleBasedResolver's "громкость/звук +
    // number" branch), so both levels take a volume command's direction from its verb with the
    // same rules. Only reached once the caller already knows the utterance is a volume command,
    // so broad stems like "выключ" are safe here. "up" is the default only when no directional
    // verb is present at all ("громкость на 30").
    public static string DetectVolumeAction(string text)
    {
        var lower = text.ToLowerInvariant();
        if (VolumeDecreaseStems.Any(lower.Contains)) return "down";
        if (VolumeMuteStems.Any(lower.Contains)) return "mute";
        if (VolumeIncreaseStems.Any(lower.Contains)) return "up";
        if (VolumeUnmuteStems.Any(lower.Contains)) return "unmute";
        return "up";
    }

    // Level 2 must extract app names using the same catalog level 1's tools already rely on
    // (AppsCatalog.FindByNameOrAlias), instead of a hardcoded list of a handful of names, so
    // any alias configured in config/apps.yaml (present or future) is recognized.
    private static string? FindAppNameFromCatalog(string text, AppsCatalog catalog)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            if (i + 1 < words.Length)
            {
                var twoWordMatch = MatchAlias(words[i] + " " + words[i + 1], catalog);
                if (twoWordMatch != null) return twoWordMatch;
            }
            var oneWordMatch = MatchAlias(words[i], catalog);
            if (oneWordMatch != null) return oneWordMatch;
        }
        return null;
    }

    // A word in the utterance may be an inflected form of an alias (Russian case endings add a
    // suffix onto the base form, e.g. "хрома" for alias "хром", "телеграма" for "телеграм"), so
    // besides an exact alias match we also accept a candidate that STARTS WITH a known alias.
    // The prefix check is guarded to aliases of at least MinPrefixAliasLength characters so a
    // short alias (e.g. "код") doesn't fuzzy-match unrelated words. Exact match is checked
    // first and is unaffected by the guard — this is a strict widening of exact matching, not a
    // replacement of it.
    private const int MinPrefixAliasLength = 4;

    private static string? MatchAlias(string candidate, AppsCatalog catalog)
    {
        if (catalog.FindByNameOrAlias(candidate) != null) return candidate;

        foreach (var app in catalog.Apps)
        {
            foreach (var alias in app.Aliases.Append(app.Name))
            {
                if (alias.Length >= MinPrefixAliasLength &&
                    candidate.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return alias;
                }
            }
        }
        return null;
    }
}
