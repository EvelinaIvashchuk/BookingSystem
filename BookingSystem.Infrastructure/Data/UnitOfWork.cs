using BookingSystem.Data.Repositories;

namespace BookingSystem.Data;

public sealed class UnitOfWork(
    ApplicationDbContext   db,
    IRentalRepository      rentals,
    ICarRepository         cars,
    IChatSessionRepository chatSessions) : IUnitOfWork
{
    public IRentalRepository      Rentals      { get; } = rentals;
    public ICarRepository         Cars         { get; } = cars;
    public IChatSessionRepository ChatSessions { get; } = chatSessions;

    public Task<int> CommitAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
