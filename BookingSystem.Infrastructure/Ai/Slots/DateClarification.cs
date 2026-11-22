using System.Text.RegularExpressions;
using BookingSystem.Ai.Tools;

namespace BookingSystem.Ai.Slots;

/// <summary>
/// Decides which rental dates the chat widget should offer as date inputs after a turn.
/// Models usually ask for missing dates in plain text without calling a tool, so slot validation never
/// sees the gap; this check looks at the reply itself: it must ask the user something (a question mark or
/// a polite request) and mention the pick-up and/or return date. Dates already known in the session are not asked.
/// </summary>
public static partial class DateClarification
{
    private static readonly string[] AskMarkers =
    [
        "?", "вкажіть", "укажіть", "уточніть", "зазначте", "напишіть", "повідомте", "підкажіть", "скажіть", "назвіть", "оберіть",
        "please", "let me know", "could you", "can you"
    ];

    private static readonly string[] PickupWords =
    [
        "отриман", "отримат", "забра", "забира", "дату видачі", "дата видачі", "день видачі", "взяття", "виїзд", "початк", "з якого", "з якої", "з коли",
        "pick-up", "pickup", "pick up", "start date", "from which", "begin"
    ];

    private static readonly string[] ReturnWords =
    [
        "поверн", "поверта", "по яке", "по який", "по яку", "до якого", "до якої", "закінч", "заверш", "кінц",
        "скільки днів", "на скільки дн", "тривал", "return", "drop-off", "drop off", "how many days", "how long", "end date", "until"
    ];

    /// <summary>Tools after which a question mentioning dates is about something else (rules, existing bookings).</summary>
    private static readonly HashSet<string> NonSearchTools =
        [BookingTools.SearchRentalPoliciesName, BookingTools.GetMyRentalsName];

    /// <summary>Date slots the reply asks the user for, in the order pick-up, return.</summary>
    public static IReadOnlyList<string> AskedDateSlots(string reply)
    {
        var text = reply.ToLowerInvariant();
        if (!AskMarkers.Any(text.Contains)) return [];

        var both = BothDatesRegex().IsMatch(text);
        var pickup = both || PickupWords.Any(text.Contains);
        var ret = both || ReturnWords.Any(text.Contains);

        return (pickup, ret) switch
        {
            (true, true) => [SlotNames.PickupDate, SlotNames.ReturnDate],
            (true, false) => [SlotNames.PickupDate],
            (false, true) => [SlotNames.ReturnDate],
            _ => []
        };
    }

    /// <summary>
    /// Date inputs to offer after a turn: the dates a blocked tool call was missing, otherwise the dates the reply
    /// asks for, minus those already known. Nothing when the turn showed cars, prepared a draft or answered about
    /// rules or the user's bookings.
    /// </summary>
    public static IReadOnlyList<string> DatesToAsk(AgentTurnContext turn, string reply)
    {
        if (turn.Cars.Count > 0 || turn.Draft is not null)
            return [];

        var blocked = turn.MissingSlots.Where(IsDateSlot).ToList();
        if (blocked.Count > 0)
            return blocked;

        if (turn.ToolCalls.Any(c => NonSearchTools.Contains(c.Name)))
            return [];

        return AskedDateSlots(reply).Where(slot => turn.Slots.Get(slot) is null).ToList();
    }

    private static bool IsDateSlot(string slot) => slot is SlotNames.PickupDate or SlotNames.ReturnDate;

    /// <summary>"дати" (plural), "dates", "період оренди", "rental period" — both dates at once.</summary>
    [GeneratedRegex(@"\bдати\b(?!\s+(?:отриман|поверн|початк|видач|закінч))|\bdates\b|період|rental period|which days", RegexOptions.CultureInvariant)]
    private static partial Regex BothDatesRegex();
}
