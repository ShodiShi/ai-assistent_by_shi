using System.Text.RegularExpressions;

namespace Jarvis.Core.Nlu;

public static class RussianNumberParser
{
    private static readonly Dictionary<string, int> Words = new()
    {
        ["один"] = 1, ["два"] = 2, ["три"] = 3, ["четыре"] = 4, ["пять"] = 5,
        ["шесть"] = 6, ["семь"] = 7, ["восемь"] = 8, ["девять"] = 9, ["десять"] = 10,
        ["одиннадцать"] = 11, ["двенадцать"] = 12, ["тринадцать"] = 13, ["четырнадцать"] = 14,
        ["пятнадцать"] = 15, ["двадцать"] = 20, ["тридцать"] = 30, ["сорок"] = 40,
        ["пятьдесят"] = 50, ["шестьдесят"] = 60, ["семьдесят"] = 70, ["восемьдесят"] = 80,
        ["девяносто"] = 90, ["сто"] = 100,
    };

    private static bool TryFindNumber(string text, out int value)
    {
        var digitMatch = Regex.Match(text, @"\d+");
        if (digitMatch.Success)
        {
            value = int.Parse(digitMatch.Value);
            return true;
        }

        foreach (var word in text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Words.TryGetValue(word, out var wordValue))
            {
                value = wordValue;
                return true;
            }
        }

        value = 0;
        return false;
    }

    public static bool TryExtractPercent(string text, out int value) => TryFindNumber(text, out value);

    public static bool TryExtractMinutes(string text, out int value) => TryFindNumber(text, out value);
}
