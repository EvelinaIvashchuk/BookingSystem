using System.Globalization;
using BookingSystem.Enums;
using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data.Repositories;

public class CarRepository(ApplicationDbContext db) : GenericRepository<Car>(db), ICarRepository
{
    public async Task<IEnumerable<Car>> GetAllWithCategoryAsync() =>
        await Db.Cars
            .Include(c => c.Category)
            .OrderBy(c => c.Category.Name)
            .ThenBy(c => c.Brand)
            .ThenBy(c => c.Model)
            .ToListAsync();

    public async Task<IEnumerable<Car>> GetAvailableWithCategoryAsync() =>
        await Db.Cars
            .Where(c => c.Status == CarStatus.Available)
            .Include(c => c.Category)
            .OrderBy(c => c.Category.Name)
            .ThenBy(c => c.Brand)
            .ThenBy(c => c.Model)
            .ToListAsync();

    public async Task<Car?> GetWithCategoryAsync(int id) =>
        await Db.Cars
            .Include(c => c.Category)
            .FirstOrDefaultAsync(c => c.Id == id);

    public async Task<bool> IsRentableAsync(int carId) =>
        await Db.Cars
            .AnyAsync(c => c.Id == carId && c.Status == CarStatus.Available);

    public async Task<IReadOnlyList<Car>> SearchAvailableAsync(CarSearchCriteria criteria, CancellationToken ct = default)
    {
        var busyCarIds = await Db.Rentals
            .Where(RentalPredicates.ActiveOverlapping(criteria.PickupDate, criteria.ReturnDate))
            .Select(r => r.CarId)
            .Distinct()
            .ToListAsync(ct);

        var query = Db.Cars
            .Include(c => c.Category)
            .Where(c => c.Status == CarStatus.Available && !busyCarIds.Contains(c.Id));

        if (criteria.CategoryId is { } categoryId)
            query = query.Where(c => c.CategoryId == categoryId);
        if (criteria.Transmission is { } transmission)
            query = query.Where(c => c.Transmission == transmission);
        if (criteria.FuelType is { } fuelType)
            query = query.Where(c => c.FuelType == fuelType);
        if (criteria.MinSeats is { } minSeats)
            query = query.Where(c => c.Seats >= minSeats);
        if (criteria.MaxPricePerDay is { } maxPrice)
            query = query.Where(c => c.PricePerDay <= maxPrice);

        return await query
            .OrderBy(c => c.PricePerDay)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);
    }

    public async Task<PagedResult<Car>> SearchCatalogAsync(
        CarCatalogQuery criteria, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Db.Cars
            .Include(c => c.Category)
            .Where(c => c.Status == CarStatus.Available);

        if (criteria is { PickupDate: { } pickup, ReturnDate: { } ret })
        {
            var busyCarIds = await Db.Rentals
                .Where(RentalPredicates.ActiveOverlapping(pickup, ret))
                .Select(r => r.CarId)
                .Distinct()
                .ToListAsync(ct);
            query = query.Where(c => !busyCarIds.Contains(c.Id));
        }

        // Each word must appear somewhere, so "toyota rav4" or "SUV Львів" find the car.
        // ToLower on both sides keeps the match case-insensitive whatever the database collation.
        foreach (var word in SplitWords(criteria.Search))
        {
            query = query.Where(c =>
                c.Brand.ToLower().Contains(word) ||
                c.Model.ToLower().Contains(word) ||
                c.Location.ToLower().Contains(word) ||
                (c.Description != null && c.Description.ToLower().Contains(word)));
        }

        if (criteria.CategoryId is { } categoryId)
            query = query.Where(c => c.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(criteria.City))
        {
            var city = criteria.City.Trim();
            var cityPrefix = city + ",";
            query = query.Where(c => c.Location == city || c.Location.StartsWith(cityPrefix));
        }
        if (criteria.Transmission is { } transmission)
            query = query.Where(c => c.Transmission == transmission);
        if (criteria.FuelType is { } fuelType)
            query = query.Where(c => c.FuelType == fuelType);
        if (criteria.MinSeats is { } minSeats)
            query = query.Where(c => c.Seats >= minSeats);
        if (criteria.MaxPricePerDay is { } maxPrice)
            query = query.Where(c => c.PricePerDay <= maxPrice);

        query = criteria.Sort switch
        {
            CarSort.PriceAsc => query.OrderBy(c => c.PricePerDay).ThenBy(c => c.Id),
            CarSort.PriceDesc => query.OrderByDescending(c => c.PricePerDay).ThenBy(c => c.Id),
            CarSort.Newest => query.OrderByDescending(c => c.Year).ThenBy(c => c.PricePerDay).ThenBy(c => c.Id),
            _ => query.OrderBy(c => c.Category.Name).ThenBy(c => c.Brand).ThenBy(c => c.Model).ThenBy(c => c.Id)
        };

        pageSize = Math.Max(1, pageSize);
        var total = await query.CountAsync(ct);
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, lastPage);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Car>(items, total, page);
    }

    public async Task<IReadOnlyList<string>> GetCitiesAsync(CancellationToken ct = default)
    {
        var locations = await Db.Cars
            .Where(c => c.Status == CarStatus.Available)
            .Select(c => c.Location)
            .Distinct()
            .ToListAsync(ct);

        return locations
            .Select(CityOf)
            .Where(city => city.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Create(CultureInfo.GetCultureInfo("uk-UA"), ignoreCase: true))
            .ToList();
    }

    /// <summary>"Київ, вул. Хрещатик, 10" → "Київ".</summary>
    public static string CityOf(string location) => location.Split(',')[0].Trim();

    private static IEnumerable<string> SplitWords(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? []
            : search.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct();
}
