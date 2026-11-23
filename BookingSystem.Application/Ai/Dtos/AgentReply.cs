namespace BookingSystem.Ai.Dtos;

/// <summary>Result of one agent turn, rendered by the chat widget.</summary>
public sealed record AgentReply
{
    public required string Text { get; init; }

    /// <summary>
    /// Session the turn belongs to. May differ from the requested one when it was unknown
    /// or owned by another user; the widget must store this value.
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>Cars to show as cards (filled by the car search tool).</summary>
    public IReadOnlyList<CarCardDto> Cars { get; init; } = [];

    /// <summary>Set when the agent prepared a booking that waits for the user's confirmation.</summary>
    public Guid? PendingActionId => Draft?.PendingActionId;

    /// <summary>Booking draft to show as a confirmation card.</summary>
    public RentalDraft? Draft { get; init; }

    /// <summary>Required parameters the agent asked the user for.</summary>
    public IReadOnlyList<string> MissingSlots { get; init; } = [];

    /// <summary>
    /// Rental dates the reply asks the user for (PickupDate / ReturnDate); the widget offers date inputs for them.
    /// </summary>
    public IReadOnlyList<string> AskedDates { get; init; } = [];

    public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = [];

    /// <summary>
    /// Amounts the model wrote that no tool returned (removed from <see cref="Text"/>); for logs and experiments.
    /// </summary>
    public IReadOnlyList<decimal> UngroundedAmounts { get; init; } = [];

    public string Model { get; init; } = string.Empty;
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public long LatencyMs { get; init; }

    /// <summary>True when the provider was unavailable and a fallback text was returned.</summary>
    public bool IsDegraded { get; init; }

    /// <summary>Provider error behind a degraded reply (for logs and experiments, not for users).</summary>
    public string? ErrorDetail { get; init; }
}

public sealed record CarCardDto(
    int Id,
    string Name,
    decimal PricePerDay,
    decimal? TotalPrice,
    string? ImageUrl);

/// <summary>One tool call of a turn, with the data needed for the experiments.</summary>
/// <param name="ArgumentsJson">Arguments exactly as the model produced them.</param>
public sealed record ToolCallRecord(
    string Name,
    string ArgumentsJson,
    bool Success,
    long DurationMs,
    string? Error = null)
{
    /// <summary>Arguments the tool actually received after slot merge and normalization.</summary>
    public string? EffectiveArgumentsJson { get; init; }

    /// <summary>The call was blocked by slot validation; the tool did not run.</summary>
    public bool Intercepted { get; init; }

    /// <summary>Slots filled in from the conversation state.</summary>
    public IReadOnlyList<string>? MergedSlots { get; init; }

    /// <summary>
    /// Validator verdict on the model's own arguments (before merge). Recorded in both modes
    /// (shadow mode when slot filling is off) so invalid-call rates are comparable.
    /// </summary>
    public IReadOnlyList<string>? ProposedMissing { get; init; }
    public IReadOnlyList<string>? ProposedInvalid { get; init; }

    public bool ProposedValid => (ProposedMissing?.Count ?? 0) == 0 && (ProposedInvalid?.Count ?? 0) == 0;
}
