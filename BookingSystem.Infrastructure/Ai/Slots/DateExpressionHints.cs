using System.Text.RegularExpressions;

namespace BookingSystem.Ai.Slots;

/// <summary>
/// Extracts verifiable facts about dates from the user's own words (Ukrainian / English):
/// mentioned weekdays and an explicit rental duration. Used to catch date arithmetic
/// mistakes of the model deterministically ("next Monday" resolved to a Wednesday).
/// </summary>
public static partial class DateExpressionHints
{
    private static readonly (Regex Pattern, DayOfWeek Day)[] Weekdays =
    [
        (MondayRegex(), DayOfWeek.Monday),
        (TuesdayRegex(), DayOfWeek.Tuesday),
        (WednesdayRegex(), DayOfWeek.Wednesday),
        (ThursdayRegex(), DayOfWeek.Thursday),
        (FridayRegex(), DayOfWeek.Friday),
        (SaturdayRegex(), DayOfWeek.Saturday),
        (SundayRegex(), DayOfWeek.Sunday)
    ];

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["один"] = 1, ["одну"] = 1, ["одна"] = 1, ["два"] = 2, ["дві"] = 2, ["три"] = 3, ["чотири"] = 4,
        ["п'ять"] = 5, ["пять"] = 5, ["шість"] = 6, ["сім"] = 7, ["вісім"] = 8, ["дев'ять"] = 9, ["десять"] = 10,
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["a"] = 1
    };

    /// <summary>Weekdays named in the text, in any grammatical case.</summary>
    public static IReadOnlySet<DayOfWeek> MentionedWeekdays(string? text)
    {
        var result = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var (pattern, day) in Weekdays)
            if (pattern.IsMatch(text))
                result.Add(day);

        return result;
    }

    /// <summary>
    /// Rental duration stated explicitly ("на 3 дні", "на три доби", "for 5 days", "на тиждень").
    /// Returns <c>null</c> when there is none or the text contains conflicting durations.
    /// "через 3 дні" / "in 3 days" describe a start date, not a duration, and are ignored.
    /// </summary>
    public static int? MentionedDurationDays(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = text.Replace('’', '\'').Replace('ʼ', '\'');
        var found = new HashSet<int>();

        foreach (Match m in DaysRegex().Matches(normalized))
        {
            var number = m.Groups["num"].Value;
            if (int.TryParse(number, out var n) || NumberWords.TryGetValue(number, out n))
                if (n is > 0 and <= 60) found.Add(n);
        }

        foreach (Match m in WeeksRegex().Matches(normalized))
        {
            var number = m.Groups["num"].Success ? m.Groups["num"].Value : "один";
            if (int.TryParse(number, out var n) || NumberWords.TryGetValue(number, out n))
                found.Add(n * 7);
        }

        return found.Count == 1 ? found.First() : null;
    }

    [GeneratedRegex(@"\b(понеділ\w*|monday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MondayRegex();

    [GeneratedRegex(@"\b(вівтор\w*|tuesday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TuesdayRegex();

    // "серед" alone means "among", so only the inflected weekday forms are matched.
    [GeneratedRegex(@"\b(серед[аиуі]|wednesday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WednesdayRegex();

    [GeneratedRegex(@"\b(четвер\w*|thursday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ThursdayRegex();

    [GeneratedRegex(@"\b(п['’ʼ`]?ятниц\w*|friday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FridayRegex();

    [GeneratedRegex(@"\b(субот\w*|saturday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SaturdayRegex();

    [GeneratedRegex(@"\b(неділ\w*|sunday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SundayRegex();

    [GeneratedRegex(
        @"(?<!через\s)(?<!\bin\s)\b(?<num>\d{1,2}|один|одну|одна|два|дві|три|чотири|п'ять|пять|шість|сім|вісім|дев'ять|десять|one|two|three|four|five|six|seven|eight|nine|ten|a)\s*-?\s*(?:дн\w*|день|доб\w*|days?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex DaysRegex();

    [GeneratedRegex(
        @"(?:\bна|\bfor)\s+(?:(?<num>\d{1,2}|один|одну|два|дві|три|one|two|three|a)\s+)?(?:тижд\w*|тижн\w*|weeks?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex WeeksRegex();
}
