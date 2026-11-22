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

    /// <summary>
    /// One page of the catalog: cars with status Available matching <paramref name="query"/>.
    /// A page past the end returns the last page.
    /// </summary>
    Task<PagedResult<Car>> SearchCatalogAsync(CarCatalogQuery query, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Distinct cities of cars with status Available, alphabetically.</summary>
    Task<IReadOnlyList<string>> GetCitiesAsync(CancellationToken ct = default);
}
