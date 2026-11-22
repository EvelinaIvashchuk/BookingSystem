using BookingSystem.Enums;

namespace BookingSystem.Models;

/// <summary>
/// An action prepared by the AI agent that only the user can confirm (human-in-the-loop).
/// The agent creates it; <c>POST /Ai/Confirm/{id}</c> executes it.
/// </summary>
public class PendingAction
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }
    public ChatSession Session { get; set; } = null!;

    /// <summary>Owner taken from the authenticated user's claims, never from the model.</summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public PendingActionType Type { get; set; } = PendingActionType.CreateRental;

    /// <summary>Action parameters (e.g. car id and dates) serialized as JSON.</summary>
    public string PayloadJson { get; set; } = "{}";

    public PendingActionStatus Status { get; set; } = PendingActionStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    /// <summary>When the user confirmed or cancelled the action.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Rental created on confirmation.</summary>
    public int? RentalId { get; set; }
}
