using System.Text.RegularExpressions;

namespace BookingSystem.Ai.Slots;

/// <summary>
/// Checks that a date the model put into a tool call is <b>grounded</b> in what the user actually said, i.e. that the
/// model did not invent it (E1, conclusion 5: on "I need a car from December 10" a model set the return date to
/// December 11 on its own and searched instead of asking).
/// <para>
/// A date is grounded when the conversation contains at least one piece of evidence for it:
/// </para>
/// <list type="bullet">
/// <item>its day of month as a number or an ordinal word ("з 10 по 12 грудня", "12.12", "December 12th", "дванадцятого");</item>
/// <item>its weekday ("до неділі"), "сьогодні/завтра/післязавтра", "через N днів", "вихідні", "наступного тижня";</item>
/// <item>for the return date — any stated rental length ("на 3 дні", "на тиждень", "на місяць") or the end of a period;</item>
/// <item>a relative change of a date that is already known ("на день довше", "краще раніше");</item>
/// <item>the same value already stored in the session slots (validated by an earlier call);</item>
/// <item>the assistant's previous reply, when it proposed the date and the user answered it ("Так, підходить").</item>
/// </list>
/// The check is deliberately lenient: it only proves that the user said <i>something</i> about the date. Whether the
/// value is computed correctly is checked separately (weekdays and duration in <see cref="SlotValidator"/>).
/// </summary>
public static partial class DateGrounding
{
    public static bool IsGrounded(DateOnly date, bool isReturnDate, DateGroundingContext context)
    {
        if (context.KnownValue == date)
            return true;

        foreach (var text in context.UserMessages)
            if (Supports(Normalize(text), date, isReturnDate, context.Today, context.KnownValue is not null))
                return true;

        // A date the assistant proposed itself counts only by its explicit day or weekday.
        return context.PreviousAssistantMessage is { } proposal
               && (MentionsDay(Normalize(proposal), date.Day) || DateExpressionHints.MentionedWeekdays(proposal).Contains(date.DayOfWeek));
    }

    private static bool Supports(string text, DateOnly date, bool isReturnDate, DateOnly today, bool dateKnown)
    {
        if (MentionsDay(text, date.Day))
            return true;

        if (DateExpressionHints.MentionedWeekdays(text).Contains(date.DayOfWeek))
            return true;

        if (RelativeDay(text, today) is { } relative && relative.Contains(date))
            return true;

        if (WeekendRegex().IsMatch(text) && date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday or DayOfWeek.Monday)
            return true;

        if (NextWeekRegex().IsMatch(text) && IsInNextWeek(date, today))
            return true;

        if (dateKnown && ShiftRegex().IsMatch(text))
            return true;

        return isReturnDate && (DurationRegex().IsMatch(text) || PeriodEndRegex().IsMatch(text));
    }

    /// <summary>The day of month as a standalone number (not seats, prices, car numbers or times) or an ordinal word.</summary>
    private static bool MentionsDay(string text, int day)
    {
        foreach (Match m in DayNumberRegex().Matches(text))
            if (int.TryParse(m.Groups["n"].Value, out var n) && n == day)
                return true;

        foreach (Match m in OrdinalRegex().Matches(text))
            if (OrdinalValue(m) == day)
                return true;

        return false;
    }

    private static HashSet<DateOnly>? RelativeDay(string text, DateOnly today)
    {
        HashSet<DateOnly>? days = null;
        void Add(DateOnly d) => (days ??= []).Add(d);

        if (TodayRegex().IsMatch(text)) Add(today);
        if (TomorrowRegex().IsMatch(text)) Add(today.AddDays(1));
        if (DayAfterTomorrowRegex().IsMatch(text)) Add(today.AddDays(2));

        foreach (Match m in InDaysRegex().Matches(text))
        {
            var number = m.Groups["num"].Value;
            if (int.TryParse(number, out var n) || CardinalWords.TryGetValue(number, out n))
                Add(today.AddDays(n));
        }

        return days;
    }

    private static bool IsInNextWeek(DateOnly date, DateOnly today)
    {
        var daysToMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        var nextMonday = today.AddDays(daysToMonday == 0 ? 7 : daysToMonday);
        return date >= nextMonday && date < nextMonday.AddDays(7);
    }

    private static string Normalize(string text) =>
        text.ToLowerInvariant().Replace('’', '\'').Replace('ʼ', '\'').Replace('`', '\'');

    // ── Ordinal words ───────────────────────────────────────────────────────

    private static readonly Dictionary<string, int> CardinalWords = new()
    {
        ["один"] = 1, ["одну"] = 1, ["два"] = 2, ["дві"] = 2, ["три"] = 3, ["чотири"] = 4, ["п'ять"] = 5,
        ["шість"] = 6, ["сім"] = 7, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5
    };

    /// <summary>Stems of Ukrainian ordinal numerals 1–20 and 30 ("десят-ого", "двадцят-е").</summary>
    private static readonly Dictionary<string, int> UkOrdinalStems = new()
    {
        ["перш"] = 1, ["друг"] = 2, ["трет"] = 3, ["четверт"] = 4, ["п'ят"] = 5, ["шост"] = 6, ["сьом"] = 7,
        ["восьм"] = 8, ["дев'ят"] = 9, ["десят"] = 10, ["одинадцят"] = 11, ["дванадцят"] = 12, ["тринадцят"] = 13,
        ["чотирнадцят"] = 14, ["п'ятнадцят"] = 15, ["шістнадцят"] = 16, ["сімнадцят"] = 17, ["вісімнадцят"] = 18,
        ["дев'ятнадцят"] = 19, ["двадцят"] = 20, ["тридцят"] = 30
    };

    private static readonly Dictionary<string, int> EnOrdinals = new()
    {
        ["first"] = 1, ["second"] = 2, ["third"] = 3, ["fourth"] = 4, ["fifth"] = 5, ["sixth"] = 6, ["seventh"] = 7,
        ["eighth"] = 8, ["ninth"] = 9, ["tenth"] = 10, ["eleventh"] = 11, ["twelfth"] = 12, ["thirteenth"] = 13,
        ["fourteenth"] = 14, ["fifteenth"] = 15, ["sixteenth"] = 16, ["seventeenth"] = 17, ["eighteenth"] = 18,
        ["nineteenth"] = 19, ["twentieth"] = 20, ["thirtieth"] = 30
    };

    private static int? OrdinalValue(Match m)
    {
        if (m.Groups["en"].Success)
        {
            var tens = m.Groups["entens"].Success ? (m.Groups["entens"].Value == "twenty" ? 20 : 30) : 0;
            return EnOrdinals.TryGetValue(m.Groups["en"].Value, out var unit) ? tens + unit : null;
        }

        var ukTens = m.Groups["uktens"].Success ? (m.Groups["uktens"].Value.StartsWith("двадцят") ? 20 : 30) : 0;
        return UkOrdinalStems.TryGetValue(m.Groups["uk"].Value, out var value) ? ukTens + value : null;
    }

    // ── Patterns ────────────────────────────────────────────────────────────

    // 1–2 digits, optionally "-го"/"th"; not part of a longer number, not a time ("10:00"), not a car number ("№17"),
    // not followed by a unit (seats, people, money, km, years, horsepower).
    [GeneratedRegex(
        @"(?<!\d)(?<!\d:)(?<!(?:№|#|номер\w*|id)\s*)(?<n>\d{1,2})(?:-?(?:го|е|ого|st|nd|rd|th))?(?!\d)(?!:\d)(?!\s*(?:-\s*)?(?:місц|місн|міс\b|осіб|особ|люд|пасаж|грн|грив|uah|₴|\$|€|%|км|km|seat|people|person|passeng|рок|рік|year|к\.?с|hp|тис|k\b|л\b|дн|день|доб|day|night|ноч|тижн|тижд|week|місяц|month|год|hour|хв|min))",
        RegexOptions.CultureInvariant)]
    private static partial Regex DayNumberRegex();

    [GeneratedRegex(
        @"\b(?:(?<uktens>двадцят[ьи]|тридцят[ьи])\s+)?(?<uk>перш|друг|трет|четверт|п'ятнадцят|п'ят|шістнадцят|шост|сімнадцят|сьом|вісімнадцят|восьм|дев'ятнадцят|дев'ят|десят|одинадцят|дванадцят|тринадцят|чотирнадцят|двадцят|тридцят)(?:ого|ому|ий|е|є|а|у|ої|ій|им)\b" +
        @"|\b(?:(?<entens>twenty|thirty)[-\s])?(?<en>first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth|thirteenth|fourteenth|fifteenth|sixteenth|seventeenth|eighteenth|nineteenth|twentieth|thirtieth)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex OrdinalRegex();

    [GeneratedRegex(@"\b(сьогодні|сьогодня|today|tonight)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TodayRegex();

    [GeneratedRegex(@"(?<!після)\bзавтра\b|\btomorrow\b", RegexOptions.CultureInvariant)]
    private static partial Regex TomorrowRegex();

    [GeneratedRegex(@"\bпіслязавтра\b|\bday after tomorrow\b", RegexOptions.CultureInvariant)]
    private static partial Regex DayAfterTomorrowRegex();

    [GeneratedRegex(@"(?:\bчерез|\bin)\s+(?<num>\d{1,2}|один|одну|два|дві|три|чотири|п'ять|шість|сім|one|two|three|four|five)\s*(?:дн\w*|день|доб\w*|days?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex InDaysRegex();

    [GeneratedRegex(@"вихідн|\bweekend", RegexOptions.CultureInvariant)]
    private static partial Regex WeekendRegex();

    [GeneratedRegex(@"наступн\w*\s+тижн|\bnext week\b", RegexOptions.CultureInvariant)]
    private static partial Regex NextWeekRegex();

    // A stated rental length: "на 3 дні", "на добу", "на тиждень", "на місяць", "for 5 days", "for a week";
    // a price per day ("1500 грн на добу", "1200 UAH a day") is not a length.
    [GeneratedRegex(
        @"(?<!(?:грн|грив\w*|uah|₴|usd|\$|€|\d)\s*)(?:\d{1,2}|\bодин|\bодну|\bдва|\bдві|\bтри|\bчотири|\bп'ять|\bшість|\bсім|\bвісім|\bдев'ять|\bдесять|\bone|\btwo|\bthree|\bfour|\bfive|\bsix|\bseven|\bten|\ba|\bна|\bfor)\s*-?\s*(?:дн\w*|день|доб\w*|days?|night\w*|ноч\w*|тижн\w*|тижд\w*|weeks?|місяц\w*|month\w*)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"кін(?:ець|ця|цем)\s+(?:тижн|місяц|рок)|end of (?:the )?(?:week|month|year)", RegexOptions.CultureInvariant)]
    private static partial Regex PeriodEndRegex();

    // Relative change of a known date: "на день довше", "краще раніше", "продовжити", "a day longer", "earlier".
    [GeneratedRegex(@"довше|довш\w*|коротш\w*|раніше|пізніше|продовж\w*|зсун\w*|перенес\w*|longer|shorter|earlier|later|extend\w*|shift\w*|move it", RegexOptions.CultureInvariant)]
    private static partial Regex ShiftRegex();
}

/// <param name="Today">Business-local date.</param>
/// <param name="UserMessages">User messages of the conversation that are still in the history, including the current one.</param>
/// <param name="PreviousAssistantMessage">The assistant's last reply before the current user message.</param>
/// <param name="KnownValue">The value stored in the session slots for this date, if any.</param>
public sealed record DateGroundingContext(
    DateOnly Today,
    IReadOnlyList<string> UserMessages,
    string? PreviousAssistantMessage = null,
    DateOnly? KnownValue = null);
