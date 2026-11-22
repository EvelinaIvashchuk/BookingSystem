namespace BookingSystem.Ai;

/// <summary>
/// Executes or discards actions prepared by the agent. Called only from the user's
/// explicit click in the widget — the model has no tool for this.
/// </summary>
public interface IPendingActionService
{
    Task<PendingActionOutcome> ConfirmAsync(Guid actionId, string userId, CancellationToken ct = default);

    Task<PendingActionOutcome> CancelAsync(Guid actionId, string userId, CancellationToken ct = default);
}

public enum PendingActionOutcomeStatus
{
    Confirmed,
    Cancelled,
    NotFound,
    Forbidden,
    Expired,
    AlreadyProcessed,

    /// <summary>Business rules rejected the booking at confirmation time (e.g. car taken meanwhile).</summary>
    Failed
}

public sealed record PendingActionOutcome(PendingActionOutcomeStatus Status, string? Error = null, int? RentalId = null)
{
    public bool IsSuccess => Status is PendingActionOutcomeStatus.Confirmed or PendingActionOutcomeStatus.Cancelled;
}
