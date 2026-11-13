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
    public Guid? PendingActionId { get; init; }

    /// <summary>Required parameters the agent asked the user for.</summary>
    public IReadOnlyList<string> MissingSlots { get; init; } = [];

    public IReadOnlyList<ToolCallRecord> ToolCalls { get; init; } = [];

    public string Model { get; init; } = string.Empty;
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public long LatencyMs { get; init; }

    /// <summary>True when the provider was unavailable and a fallback text was returned.</summary>
    public bool IsDegraded { get; init; }
}

public sealed record CarCardDto(
    int Id,
    string Name,
    decimal PricePerDay,
    decimal? TotalPrice,
    string? ImageUrl);

public sealed record ToolCallRecord(
    string Name,
    string ArgumentsJson,
    bool Success,
    long DurationMs,
    string? Error = null);
