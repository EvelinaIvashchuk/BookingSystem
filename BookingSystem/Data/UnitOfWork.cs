using BookingSystem.Data.Repositories;

namespace BookingSystem.Data;

/// <summary>
/// Реалізація Unit of Work.
/// Отримує репозиторії через DI та делегує збереження
/// в ApplicationDbContext через CommitAsync().
/// </summary>
public sealed class UnitOfWork(
    ApplicationDbContext db,
    IBookingRepository   bookings,
    IResourceRepository  resources) : IUnitOfWork
{
    /// <inheritdoc/>
    public IBookingRepository  Bookings  { get; } = bookings;

    /// <inheritdoc/>
    public IResourceRepository Resources { get; } = resources;

    /// <inheritdoc/>
    public Task<int> CommitAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => db.DisposeAsync();
}
