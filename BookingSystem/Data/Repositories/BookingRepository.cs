using BookingSystem.Enums;
using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data.Repositories;

public class BookingRepository(ApplicationDbContext db)
    : GenericRepository<Booking>(db), IBookingRepository
{
    public async Task<IEnumerable<Booking>> GetUserBookingsAsync(string userId) =>
        await Db.Bookings
            .Where(b => b.UserId == userId)
            .Include(b => b.Resource)
                .ThenInclude(r => r.Category)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

    public async Task<IEnumerable<Booking>> GetAllWithDetailsAsync() =>
        await Db.Bookings
            .Include(b => b.User)
            .Include(b => b.Resource)
                .ThenInclude(r => r.Category)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

    public async Task<Booking?> GetWithDetailsAsync(int bookingId) =>
        await Db.Bookings
            .Include(b => b.User)
            .Include(b => b.Resource)
                .ThenInclude(r => r.Category)
            .FirstOrDefaultAsync(b => b.Id == bookingId);

    public async Task<bool> HasOverlapAsync(
        int      resourceId,
        DateTime start,
        DateTime end,
        int?     excludeBookingId = null)
    {
        // Standard interval overlap: A.start < B.end AND A.end > B.start
        // Only active (Pending / Confirmed) bookings block the slot.
        return await Db.Bookings
            .Where(b =>
                b.ResourceId == resourceId &&
                (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed) &&
                (excludeBookingId == null || b.Id != excludeBookingId) &&
                b.StartTime < end &&
                b.EndTime   > start)
            .AnyAsync();
    }

    public async Task<int> GetActiveBookingCountAsync(string userId) =>
        await Db.Bookings
            .CountAsync(b =>
                b.UserId == userId &&
                (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed));
}
