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

                string action;
                if (text.Contains("тиш") || text.Contains("убав"))
                    action = "down";
                else if (text.Contains("глуш") || text.Contains("выруби"))
                    action = "mute";
                else if (text.Contains("включи звук"))
                    action = "unmute";
                else
                    action = "up";
                args["action"] = action;
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
