using BookingSystem.Enums;
using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data.Repositories;

public class RentalRepository(ApplicationDbContext db)
    : GenericRepository<Rental>(db), IRentalRepository
{
    public async Task<IEnumerable<Rental>> GetUserRentalsAsync(string userId) =>
        await Db.Rentals
            .Where(r => r.UserId == userId)
            .Include(r => r.Car)
                .ThenInclude(c => c.Category)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

    public async Task<IEnumerable<Rental>> GetAllWithDetailsAsync() =>
        await Db.Rentals
            .Include(r => r.User)
            .Include(r => r.Car)
                .ThenInclude(c => c.Category)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

    public async Task<Rental?> GetWithDetailsAsync(int rentalId) =>
        await Db.Rentals
            .Include(r => r.User)
            .Include(r => r.Car)
                .ThenInclude(c => c.Category)
            .FirstOrDefaultAsync(r => r.Id == rentalId);

    public async Task<bool> HasOverlapAsync(int carId, DateTime pickupDate, DateTime returnDate, int? excludeRentalId = null)
    {
        return await Db.Rentals
            .Where(r =>
                r.CarId == carId &&
                (r.Status == RentalStatus.Pending || r.Status == RentalStatus.Confirmed) &&
                (excludeRentalId == null || r.Id != excludeRentalId) &&
                r.PickupDate < returnDate &&
                r.ReturnDate > pickupDate)
            .AnyAsync();
    }

    public async Task<int> GetActiveRentalCountAsync(string userId) =>
        await Db.Rentals
            .CountAsync(r =>
                r.UserId == userId &&
                (r.Status == RentalStatus.Pending || r.Status == RentalStatus.Confirmed));

    public async Task<IEnumerable<(DateTime Pickup, DateTime Return)>> GetBookedRangesForCarAsync(int carId)
    {
        var rows = await Db.Rentals
            .Where(r => r.CarId == carId &&
                        (r.Status == RentalStatus.Pending || r.Status == RentalStatus.Confirmed) &&
                        r.PickupDate != null && r.ReturnDate != null)
            .Select(r => new { r.PickupDate, r.ReturnDate })
            .ToListAsync();

        return rows.Select(r => (r.PickupDate!.Value, r.ReturnDate!.Value));
    }
}
