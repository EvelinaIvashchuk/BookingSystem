namespace BookingSystem.Ai.Dtos;

/// <summary>
/// A booking prepared by the agent but not created yet. Stored as the payload of a
/// <c>PendingAction</c> and shown in the widget as a confirmation card.
/// </summary>
public sealed record RentalDraft
{
    public Guid PendingActionId { get; init; }
    public int CarId { get; init; }
    public string CarName { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }

    /// <summary>Pickup date, <c>yyyy-MM-dd</c>.</summary>
    public string PickupDate { get; init; } = string.Empty;

    /// <summary>Return date, <c>yyyy-MM-dd</c>.</summary>
    public string ReturnDate { get; init; } = string.Empty;

    public int Days { get; init; }
    public decimal PricePerDay { get; init; }
    public decimal TotalPrice { get; init; }
    public string? Notes { get; init; }

    /// <summary>UTC time after which the draft can no longer be confirmed.</summary>
    public DateTime ExpiresAt { get; init; }
}
