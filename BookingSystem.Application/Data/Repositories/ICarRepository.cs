using BookingSystem.Models;

namespace BookingSystem.Data.Repositories;

public interface ICarRepository : IGenericRepository<Car>
{
    Task<IEnumerable<Car>> GetAllWithCategoryAsync();
    Task<IEnumerable<Car>> GetAvailableWithCategoryAsync();
    Task<Car?> GetWithCategoryAsync(int id);
    Task<bool> IsRentableAsync(int carId);

    /// <summary>
    /// Cars with status Available and no active rental overlapping the requested dates,
    /// matching the filters, cheapest first.
    /// </summary>
    Task<IReadOnlyList<Car>> SearchAvailableAsync(CarSearchCriteria criteria, CancellationToken ct = default);
}
