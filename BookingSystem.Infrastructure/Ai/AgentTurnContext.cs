using BookingSystem.Ai.Dtos;
using BookingSystem.Ai.Slots;

namespace BookingSystem.Ai;

/// <summary>
/// State of a single agent turn shared between <see cref="AgentService"/> and the tools:
/// who is asking (from claims, never from the model), the reference date and what the tools produced.
/// </summary>
public sealed class AgentTurnContext(
    Guid sessionId,
    string? userId,
    DateOnly today,
    string? latestUserMessage = null,
    BookingSlots? slots = null,
    bool slotFillingEnabled = true)
{
    public Guid SessionId { get; } = sessionId;

    /// <summary>Authenticated user id from claims; <c>null</c> for anonymous visitors.</summary>
    public string? UserId { get; } = userId;

    /// <summary>Business-local date (Europe/Kyiv) used for date rules.</summary>
    public DateOnly Today { get; } = today;

    /// <summary>The user's message of this turn (for consistency checks of dates).</summary>
    public string? LatestUserMessage { get; } = latestUserMessage;

    /// <summary>Booking parameters known in this conversation; updated after successful tool calls.</summary>
    public BookingSlots Slots { get; } = slots ?? new BookingSlots();

    public bool SlotFillingEnabled { get; } = slotFillingEnabled;

    /// <summary>Block dates the user never stated (<see cref="DateGrounding"/>).</summary>
    public bool RequireGroundedDates { get; init; }

    /// <summary>Earlier user messages still in the history (oldest first), for the grounding check.</summary>
    public IReadOnlyList<string> EarlierUserMessages { get; init; } = [];

    /// <summary>The assistant's reply right before the current user message.</summary>
    public string? PreviousAssistantMessage { get; init; }

    /// <summary>Context for validating a tool call; <paramref name="provided"/> are the arguments the model produced itself.</summary>
    public SlotValidationContext ValidationContext(IReadOnlySet<string> provided) =>
        new(Today, LatestUserMessage, provided)
        {
            RequireGroundedDates = RequireGroundedDates,
            EarlierUserMessages = EarlierUserMessages,
            PreviousAssistantMessage = PreviousAssistantMessage,
            KnownSlots = Slots
        };

    /// <summary>Slots the last blocked call was missing (what the agent should be asking about).</summary>
    public IReadOnlyList<string> MissingSlots { get; set; } = [];

    public List<ToolCallRecord> ToolCalls { get; } = [];

    /// <summary>Results of the tools executed in this turn (JSON): the evidence for <see cref="AmountGrounding"/>.</summary>
    public List<string> ToolOutputs { get; } = [];

    /// <summary>Cars returned by the latest successful search, shown as cards in the widget.</summary>
    public IReadOnlyList<CarCardDto> Cars { get; set; } = [];

    /// <summary>Booking draft created in this turn; the widget shows a confirmation card for it.</summary>
    public RentalDraft? Draft { get; set; }
}
