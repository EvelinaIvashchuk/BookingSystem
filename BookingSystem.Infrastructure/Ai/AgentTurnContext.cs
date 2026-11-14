using BookingSystem.Ai.Dtos;

namespace BookingSystem.Ai;

/// <summary>
/// State of a single agent turn shared between <see cref="AgentService"/> and the tools:
/// who is asking (from claims, never from the model), the reference date and what the tools produced.
/// </summary>
public sealed class AgentTurnContext(Guid sessionId, string? userId, DateOnly today)
{
    public Guid SessionId { get; } = sessionId;

    /// <summary>Authenticated user id from claims; <c>null</c> for anonymous visitors.</summary>
    public string? UserId { get; } = userId;

    /// <summary>Business-local date (Europe/Kyiv) used for date rules.</summary>
    public DateOnly Today { get; } = today;

    public List<ToolCallRecord> ToolCalls { get; } = [];

    /// <summary>Cars returned by the latest successful search, shown as cards in the widget.</summary>
    public IReadOnlyList<CarCardDto> Cars { get; set; } = [];

    /// <summary>Booking draft created in this turn; the widget shows a confirmation card for it.</summary>
    public RentalDraft? Draft { get; set; }
}
