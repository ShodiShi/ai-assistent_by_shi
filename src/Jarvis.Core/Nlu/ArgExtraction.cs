using System.Text.RegularExpressions;

namespace Jarvis.Core.Nlu;

public static class ArgExtraction
{
    public static Dictionary<string, object?> ExtractArgs(string tool, string utterance)
    {
        var args = new Dictionary<string, object?>();
        var text = utterance.ToLowerInvariant();

        switch (tool)
        {
            case "open_app":
            case "close_app":
                var match = Regex.Match(text, @"(хром|телеграм|дискорд|вс код|код|проводник|блокнот|браузер)");
                if (match.Success) args["name"] = match.Value;
                break;
            case "volume_control":
                if (RussianNumberParser.TryExtractPercent(text, out var percent))
                    args["amount"] = percent;
                args["action"] = text.Contains("тиш") || text.Contains("убав") ? "down"
                    : text.Contains("глуш") || text.Contains("выруби") ? "mute"
                    : "up";
                break;
            case "system_control":
                args["action"] = text.Contains("блок") ? "lock"
                    : text.Contains("перезагру") || text.Contains("ребут") ? "restart"
                    : text.Contains("сп") ? "sleep"
                    : "shutdown";
                break;
        }
        return args;
    }
}
