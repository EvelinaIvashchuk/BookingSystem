using BookingSystem.Data.Repositories;

namespace BookingSystem.Data;

public interface IUnitOfWork : IAsyncDisposable
{
    IRentalRepository Rentals { get; }
    ICarRepository Cars { get; }
    IChatSessionRepository ChatSessions { get; }
    Task<int> CommitAsync(CancellationToken ct = default);
}
