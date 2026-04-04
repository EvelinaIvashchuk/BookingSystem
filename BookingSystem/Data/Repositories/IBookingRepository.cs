using BookingSystem.Models;

namespace BookingSystem.Data.Repositories;

public interface IBookingRepository : IGenericRepository<Booking>
{
    /// <summary>All bookings for a user, newest first, with Resource + Category included.</summary>
    Task<IEnumerable<Booking>> GetUserBookingsAsync(string userId);

    /// <summary>All bookings in the system, with User + Resource included (admin view).</summary>
    Task<IEnumerable<Booking>> GetAllWithDetailsAsync();

    /// <summary>Single booking with User + Resource + Category included.</summary>
    Task<Booking?> GetWithDetailsAsync(int bookingId);

    /// <summary>
    /// Returns true when a Pending or Confirmed booking on <paramref name="resourceId"/>
    /// overlaps the interval [<paramref name="start"/>, <paramref name="end"/>).
    /// Pass <paramref name="excludeBookingId"/> to skip the current booking when editing.
    /// </summary>
    Task<bool> HasOverlapAsync(
        int      resourceId,
        DateTime start,
        DateTime end,
        int?     excludeBookingId = null);

    /// <summary>Count of Pending + Confirmed bookings belonging to a user.</summary>
    Task<int> GetActiveBookingCountAsync(string userId);
}
