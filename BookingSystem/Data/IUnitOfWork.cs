using BookingSystem.Data.Repositories;

namespace BookingSystem.Data;

public interface IUnitOfWork : IAsyncDisposable
{
    IRentalRepository Rentals { get; }
    ICarRepository Cars { get; }
    Task<int> CommitAsync(CancellationToken ct = default);
}
