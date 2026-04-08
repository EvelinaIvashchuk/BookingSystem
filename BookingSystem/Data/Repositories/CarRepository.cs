using BookingSystem.Enums;
using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data.Repositories;

public class CarRepository(ApplicationDbContext db)
    : GenericRepository<Car>(db), ICarRepository
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
}
