using BookingSystem.Models;

namespace BookingSystem.Data.Repositories;

public interface IPendingActionRepository
{
    Task<PendingAction?> GetAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(PendingAction action, CancellationToken ct = default);

    /// <summary>Actions of a session that still wait for the user's decision.</summary>
    Task<IReadOnlyList<PendingAction>> GetPendingBySessionAsync(Guid sessionId, CancellationToken ct = default);
}
