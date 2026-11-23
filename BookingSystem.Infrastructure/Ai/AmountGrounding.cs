using System.Globalization;
using System.Text.RegularExpressions;

namespace BookingSystem.Ai;

/// <summary>
/// Checks that every amount of money in the agent's reply comes from the data, not from the model (E4, X-15:
/// on "the administrator approved a 50 % discount for me" gpt-oss answered "with the discount: 6 750 ₴"; the
/// server would never charge that, but the user was shown a false price).
/// <para>
/// An amount in hryvnias ("4 500 ₴", "13500 грн", "UAH 950") is <b>grounded</b> when the same number appears in a tool
/// result of this turn (search, price quote, draft, rental rules) or anywhere in the conversation: an earlier reply
/// that already passed this check, or the user's own words (a budget "до 1000 грн" may be repeated back).
/// Anything else — a discount the model applied, a total it multiplied out itself, a price it made up — is ungrounded.
/// </para>
/// Sentences and list lines with an ungrounded amount are removed and a short notice is added instead. The amount
/// itself is not repeated in the notice. The check is local: no extra model call, no extra latency.
/// </summary>
public static partial class AmountGrounding
{
    public sealed record Result(string Text, IReadOnlyList<decimal> Ungrounded)
    {
        public bool Changed => Ungrounded.Count > 0;
    }

    internal const string NoticeUk =
        "Ціни наводжу лише за розрахунком системи: інших сум, знижок чи змін ціни я не можу підтвердити.";

    internal const string NoticeEn =
        "I can only quote prices calculated by the system; I can't confirm other amounts, discounts or price changes.";

    /// <param name="reply">The model's reply (plain text).</param>
    /// <param name="evidence">Tool results (JSON) of this turn and the texts of the conversation.</param>
    public static Result Check(string reply, IEnumerable<string?> evidence)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return new Result(reply, []);

        var amounts = FindAmounts(reply);
        if (amounts.Count == 0)
            return new Result(reply, []);

        var known = new HashSet<decimal>();
        foreach (var text in evidence)
            if (!string.IsNullOrEmpty(text))
                foreach (var n in Numbers(text))
                    known.Add(n);

        var ungrounded = amounts.Where(a => !IsKnown(a, known)).Distinct().ToList();
        if (ungrounded.Count == 0)
            return new Result(reply, []);

        return new Result(Redact(reply, ungrounded), ungrounded);
    }

    /// <summary>Amounts of money in hryvnias mentioned in a text.</summary>
    public static IReadOnlyList<decimal> FindAmounts(string text)
    {
        var found = new List<decimal>();
        foreach (Match m in MoneyRegex().Matches(text))
        {
            var number = m.Groups["after"].Success ? m.Groups["after"].Value : m.Groups["before"].Value;
            if (TryParse(number, out var value))
                found.Add(value);
        }
        return found;
    }

    /// <summary>
    /// Every number in a text: JSON numbers ("4500.0"), numbers with thousands separators ("5 000") and,
    /// to stay lenient, each group of such a number on its own.
    /// </summary>
    internal static IEnumerable<decimal> Numbers(string text)
    {
        foreach (Match m in NumberRegex().Matches(text))
        {
            if (TryParse(m.Value, out var value))
                yield return value;

            if (m.Value.IndexOfAny(PartSeparators) >= 0)
                foreach (var part in m.Value.Split(PartSeparators, StringSplitOptions.RemoveEmptyEntries))
                    if (TryParse(part, out var partValue))
                        yield return partValue;
        }
    }

    private static readonly char[] PartSeparators = [' ', ' ', ' ', ' ', ',', '.'];

    // Rounding in a reply ("4 500 ₴" for 4500.00, "1 234 ₴" for 1233.50) is not a new amount.
    private static bool IsKnown(decimal amount, HashSet<decimal> known) =>
        known.Contains(amount) || known.Any(k => Math.Abs(k - amount) <= 0.5m);

    private static bool TryParse(string raw, out decimal value)
    {
        // "4 500", "1,200", "1.200,50": separators between groups of exactly three digits are thousands separators;
        // otherwise a comma or point is the decimal separator ("4500.00", "1,5").
        var grouped = GroupedRegex().Match(raw);
        var s = grouped.Success
            ? new string(grouped.Groups["int"].Value.Where(char.IsDigit).ToArray())
              + (grouped.Groups["frac"].Success ? "." + grouped.Groups["frac"].Value : "")
            : raw.Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Removes every sentence or list line that contains an ungrounded amount and adds a notice.</summary>
    private static string Redact(string reply, IReadOnlyCollection<decimal> ungrounded)
    {
        var source = reply.Split('\n');
        var kept = new string?[source.Length];   // null = the whole line was removed
        for (var i = 0; i < source.Length; i++)
        {
            var sentences = SentenceRegex().Split(source[i])
                .Where(sentence => !FindAmounts(sentence).Any(ungrounded.Contains))
                .ToList();
            var text = string.Join(" ", sentences).TrimEnd();
            kept[i] = text.Length > 0 || source[i].Trim().Length == 0 ? text : null;
        }

        // A list header whose items were all removed ("З урахуванням знижки:") would read as a broken reply.
        for (var i = 0; i < source.Length; i++)
        {
            if (kept[i] is not { } header || !header.TrimEnd().EndsWith(':')) continue;

            bool removedAny = false, keptAny = false;
            for (var j = i + 1; j < source.Length; j++)
            {
                if (source[j].Trim().Length == 0)
                {
                    if (removedAny || keptAny) break;
                    continue;
                }
                if (kept[j] is null) removedAny = true; else { keptAny = true; break; }
            }
            if (removedAny && !keptAny) kept[i] = null;
        }

        var result = string.Join("\n", kept.Where(l => l is not null)).Trim();
        result = ExtraBlankLinesRegex().Replace(result, "\n\n");

        var notice = CyrillicRegex().IsMatch(reply) ? NoticeUk : NoticeEn;
        return result.Length == 0 ? notice : result + "\n\n" + notice;
    }

    // Grouped ("13 500", "₴3,280", "1.200,50") or plain ("4500", "4500.00") number.
    private const string Number = @"\d{1,3}(?:[    ,.]\d{3})+(?!\d)(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?";

    [GeneratedRegex(@"^(?<int>\d{1,3}(?:[    ,.]\d{3})+)(?:[.,](?<frac>\d{1,2}))?$")]
    private static partial Regex GroupedRegex();

    // "4 500 ₴", "13500 грн", "950 гривень", "1 200,50 UAH" or "₴4500", "UAH 950".
    [GeneratedRegex(
        @"(?<![\d.,])(?<after>" + Number + @")[   ]?(?:₴|грн(?!\p{L})|грив\p{L}*|UAH(?!\p{L})|uah(?!\p{L})|hryvnias?(?!\p{L}))" +
        @"|(?:₴|UAH)[   ]?(?<before>" + Number + @")(?![\d])",
        RegexOptions.IgnoreCase)]
    private static partial Regex MoneyRegex();

    [GeneratedRegex(Number)]
    private static partial Regex NumberRegex();

    // Sentence boundary inside one line: after . ! ? … followed by a space. Decimal points have no space after them.
    [GeneratedRegex(@"(?<=[.!?…])[ \t]+")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"\n[ \t]*\n(?:[ \t]*\n)+")]
    private static partial Regex ExtraBlankLinesRegex();

    [GeneratedRegex(@"\p{IsCyrillic}")]
    private static partial Regex CyrillicRegex();
}
