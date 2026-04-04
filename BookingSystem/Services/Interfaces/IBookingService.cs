using BookingSystem.Models;

namespace BookingSystem.Services.Interfaces;

public interface IBookingService
{
    /// <summary>
    /// Creates a booking after validating all business rules.
    /// Returns the new Booking on success, or a descriptive error.
    /// </summary>
    Task<ServiceResult<Booking>> CreateBookingAsync(
        string  userId,
        int     resourceId,
        DateTime start,
        DateTime end,
        string? purpose);

    /// <summary>
    /// Cancels a booking owned by <paramref name="userId"/>.
    /// Users may only cancel their own future bookings.
    /// </summary>
    Task<ServiceResult> CancelBookingAsync(int bookingId, string userId);

    /// <summary>Admin: confirms a Pending booking.</summary>
    Task<ServiceResult> ConfirmBookingAsync(int bookingId, string? adminNote);

    /// <summary>Admin: rejects a Pending booking. Note is mandatory.</summary>
    Task<ServiceResult> RejectBookingAsync(int bookingId, string adminNote);

    /// <summary>Returns all bookings for a user, newest first.</summary>
    Task<IEnumerable<Booking>> GetUserBookingsAsync(string userId);

    /// <summary>Returns all bookings in the system (admin view).</summary>
    Task<IEnumerable<Booking>> GetAllBookingsAsync();

    /// <summary>Returns a single booking with full navigation data, or null.</summary>
    Task<Booking?> GetBookingByIdAsync(int bookingId);
}
