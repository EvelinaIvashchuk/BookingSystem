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
}
