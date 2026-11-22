using BookingSystem.Enums;
using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data.Repositories;

public class PendingActionRepository(ApplicationDbContext db) : IPendingActionRepository
{
    public Task<PendingAction?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.PendingActions.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAsync(PendingAction action, CancellationToken ct = default) =>
        await db.PendingActions.AddAsync(action, ct);

    public async Task<IReadOnlyList<PendingAction>> GetPendingBySessionAsync(Guid sessionId, CancellationToken ct = default) =>
        await db.PendingActions
            .Where(a => a.SessionId == sessionId && a.Status == PendingActionStatus.Pending)
            .ToListAsync(ct);
}
