using BookingSystem.Data.Repositories;

namespace BookingSystem.Data;

/// <summary>
/// Unit of Work — координує репозиторії та фіксує всі зміни
/// в одній транзакції бази даних через єдиний CommitAsync().
/// Вирішує проблему неконсистентності при роботі з кількома репозиторіями.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Репозиторій для роботи з бронюваннями.</summary>
    IBookingRepository Bookings { get; }

    /// <summary>Репозиторій для роботи з ресурсами.</summary>
    IResourceRepository Resources { get; }

    /// <summary>
    /// Зберігає всі зміни, зроблені через репозиторії, в базу даних.
    /// Повертає кількість змінених рядків.
    /// </summary>
    Task<int> CommitAsync(CancellationToken ct = default);
}
