using System.Text.Json;
using System.Text.RegularExpressions;
using BookingSystem.Ai.Dtos;
using BookingSystem.Ai.Slots;

namespace BookingSystem.Evaluation;

/// <summary>Metrics of one scenario run (one model, one repeat).</summary>
public sealed record ScenarioScore
{
    public string? FirstRelevantTool { get; init; }
    public bool ToolSelectionCorrect { get; init; }
    public int ArgsExpected { get; init; }
    public int ArgsCorrect { get; init; }
    public bool ClarificationExpected { get; init; }
    public bool ClarificationCorrect { get; init; }
    public bool ForbiddenCall { get; init; }
    public bool ContentOk { get; init; }
    public bool TaskSuccess { get; init; }

    /// <summary>Calls of tools with slots (search, price, draft, details), including blocked ones.</summary>
    public int SlotToolCalls { get; init; }
    public int ProposedInvalidCalls { get; init; }
    public int ExecutedCalls { get; init; }
    public int ExecutedInvalidCalls { get; init; }
    public int InterceptedCalls { get; init; }
    public string ArgMismatches { get; init; } = string.Empty;
}

/// <summary>
/// Scores a scenario run against its expectations (the last turn is evaluated):
/// <list type="bullet">
/// <item>Tool Selection — the first executed tool that is not an optional preparatory one equals the expected tool
///       (or no tool when none is expected). Calls blocked by slot filling are not "executed".</item>
/// <item>Argument Accuracy — expected arguments of the last executed call of the expected tool (effective
///       arguments after slot merge when slot filling is on); dates, enums, cities and numbers normalized.</item>
/// <item>Clarification — for incomplete requests: no forbidden tool ran, the reply is a question and mentions
///       every missing slot (keyword heuristics, Ukrainian and English).</item>
/// <item>Task Success — all of the above plus content checks and no forbidden calls.</item>
/// <item>Invalid calls — proposed (validator verdict on the model's own arguments, both modes) and executed
///       (the tool actually ran with invalid arguments).</item>
/// </list>
/// </summary>
public static partial class ScenarioScorer
{
    private static readonly HashSet<string> SlotTools =
        ["search_available_cars", "calculate_price", "create_rental_draft", "get_car_details"];

    public static ScenarioScore Score(AgentScenario scenario, IReadOnlyList<AgentReply> replies)
    {
        var expected = scenario.Expected;
        var last = replies[^1];
        var allCalls = replies.SelectMany(r => r.ToolCalls).ToList();
        var lastExecuted = last.ToolCalls.Where(c => !c.Intercepted).ToList();

        // Tool selection
        var firstRelevant = lastExecuted.FirstOrDefault(c => !expected.OptionalTools.Contains(c.Name))?.Name;
        var toolOk = firstRelevant == expected.Tool;

        // Arguments
        var argsExpected = expected.Args.Count;
        var argsCorrect = 0;
        var mismatches = new List<string>();
        if (argsExpected > 0)
        {
            var call = lastExecuted.LastOrDefault(c => c.Name == expected.Tool && c.Success)
                       ?? lastExecuted.LastOrDefault(c => c.Name == expected.Tool);
            var actual = call is null ? [] : ParseArgs(call.EffectiveArgumentsJson ?? call.ArgumentsJson);
            foreach (var (key, value) in expected.Args)
            {
                if (actual.TryGetValue(key, out var got) && ArgEquals(key, value, got)) argsCorrect++;
                else mismatches.Add($"{key}={(actual.TryGetValue(key, out var g) ? g.ToString() : "∅")}");
            }
        }

        // Forbidden calls (executed in any turn)
        var executedAll = allCalls.Where(c => !c.Intercepted).ToList();
        var forbidden = expected.MustNotCallTools.Contains("*")
            ? executedAll.Count > 0
            : executedAll.Any(c => expected.MustNotCallTools.Contains(c.Name));

        // Clarification
        var clarificationExpected = expected.AsksFor.Count > 0;
        var clarificationOk = clarificationExpected
            && !forbidden
            && last.Text.Contains('?')
            && expected.AsksFor.All(slot => AsksAbout(last.Text, slot));

        // Content
        var text = NormalizeText(last.Text);
        var contentOk = expected.MustContain.All(s => text.Contains(NormalizeText(s)))
                        && (expected.MustContainAny.Count == 0 || expected.MustContainAny.Any(s => text.Contains(NormalizeText(s))))
                        && !expected.MustNotContain.Any(s => text.Contains(NormalizeText(s)));

        var success = !last.IsDegraded
                      && toolOk
                      && argsCorrect == argsExpected
                      && (!clarificationExpected || clarificationOk)
                      && !forbidden
                      && contentOk;

        var slotCalls = allCalls.Where(c => SlotTools.Contains(c.Name)).ToList();
        return new ScenarioScore
        {
            FirstRelevantTool = firstRelevant,
            ToolSelectionCorrect = toolOk,
            ArgsExpected = argsExpected,
            ArgsCorrect = argsCorrect,
            ClarificationExpected = clarificationExpected,
            ClarificationCorrect = clarificationOk,
            ForbiddenCall = forbidden,
            ContentOk = contentOk,
            TaskSuccess = success,
            SlotToolCalls = slotCalls.Count,
            ProposedInvalidCalls = slotCalls.Count(c => !c.ProposedValid),
            ExecutedCalls = executedAll.Count,
            ExecutedInvalidCalls = executedAll.Count(c =>
                c.Error == "invalid_arguments" || (SlotTools.Contains(c.Name) && c.EffectiveArgumentsJson is null && !c.ProposedValid)),
            InterceptedCalls = allCalls.Count(c => c.Intercepted),
            ArgMismatches = string.Join(' ', mismatches)
        };
    }

    // ── Arguments ────────────────────────────────────────────────────────────

    internal static Dictionary<string, JsonElement> ParseArgs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject()
                    .Where(p => p.Value.ValueKind != JsonValueKind.Null)
                    .ToDictionary(p => p.Name, p => p.Value.Clone())
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static bool ArgEquals(string key, JsonElement expected, JsonElement actual)
    {
        object? e = Raw(expected), a = Raw(actual);
        switch (key)
        {
            case "pickupDate":
            case "returnDate":
                return SlotValues.TryParseDate(e, out var de) && SlotValues.TryParseDate(a, out var da) && de == da;
            case "transmission":
                return BookingVocabulary.TryParseTransmission(SlotValues.AsString(e), out var te)
                       && BookingVocabulary.TryParseTransmission(SlotValues.AsString(a), out var ta) && te == ta;
            case "fuelType":
                return BookingVocabulary.TryParseFuelType(SlotValues.AsString(e), out var fe)
                       && BookingVocabulary.TryParseFuelType(SlotValues.AsString(a), out var fa) && fe == fa;
            case "category":
                return BookingVocabulary.TryNormalizeCategory(SlotValues.AsString(e), out var ce)
                       && BookingVocabulary.TryNormalizeCategory(SlotValues.AsString(a), out var ca) && ce == ca;
            case "location":
            {
                var le = BookingVocabulary.NormalizeCity(SlotValues.AsString(e) ?? "");
                var la = BookingVocabulary.NormalizeCity(SlotValues.AsString(a) ?? "");
                return la.Length > 0 && la.Contains(le, StringComparison.OrdinalIgnoreCase);
            }
            default:
                return SlotValues.TryGetDecimal(e, out var ne) && SlotValues.TryGetDecimal(a, out var na)
                    ? ne == na
                    : string.Equals(SlotValues.AsString(e), SlotValues.AsString(a), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static object? Raw(JsonElement e) => e;

    // ── Clarification ────────────────────────────────────────────────────────

    private static readonly string[] BothDates =
    [
        "дати оренди", "які дати", "на які дати", "період оренди", "дати отримання та повернення", "дату отримання та повернення",
        "дати початку та завершення", "rental dates", "which dates", "what dates", "the dates", "rental period"
    ];

    private static readonly Dictionary<string, string[]> SlotKeywords = new()
    {
        [SlotNames.PickupDate] =
        [
            "отриман", "з якого", "з якої", "початк", "коли забрати", "коли вам потрібн", "коли потрібн", "з коли",
            "коли плануєте", "коли бажаєте", "дата початку", "pick", "start", "from which", "when would you", "when do you need", "begin"
        ],
        [SlotNames.ReturnDate] =
        [
            "поверн", "по яке", "до якого", "до якої", "кінц", "заверш", "скільки днів", "на скільки", "тривал",
            "return", "until", "how many days", "how long", "end date", "drop-off", "drop off"
        ],
        [SlotNames.SelectedCarId] = ["яке авто", "який автомобіль", "яку машину", "which car", "оберіть", "which vehicle"]
    };

    internal static bool AsksAbout(string reply, string slot)
    {
        var text = reply.ToLowerInvariant();
        if (slot is SlotNames.PickupDate or SlotNames.ReturnDate && BothDates.Any(text.Contains))
            return true;
        return SlotKeywords.TryGetValue(slot, out var keywords) && keywords.Any(text.Contains);
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    /// <summary>Lower-case; "10 000", "10 000", "10,000" → "10000"; typographic apostrophes unified.</summary>
    internal static string NormalizeText(string text)
    {
        var lower = text.ToLowerInvariant().Replace('’', '\'').Replace('ʼ', '\'');
        return DigitGroupRegex().Replace(lower, "");
    }

    [GeneratedRegex(@"(?<=\d)[\s  ,.](?=\d{3}\b)")]
    private static partial Regex DigitGroupRegex();
}
