using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services.Interfaces;

namespace BookingSystem.Services;

public class BookingService(
    IBookingRepository  bookingRepo,
    IResourceRepository resourceRepo,
    IEmailService       emailService,
    ILogger<BookingService> logger) : IBookingService
{
    // ── Business rule constants ────────────────────────────────────────────────
    private const int MaxActiveBookingsPerUser = 3;
    private const int MinDurationMinutes       = 30;
    private const int MaxDurationHours         = 8;
    private const int MaxAdvanceBookingDays    = 30;

    // ── Create ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<Booking>> CreateBookingAsync(
        string   userId,
        int      resourceId,
        DateTime start,
        DateTime end,
        string?  purpose)
    {
        // 1. Basic time validation
        var timeCheck = ValidateTimes(start, end);
        if (!timeCheck.IsSuccess)
            return ServiceResult<Booking>.Fail(timeCheck.Error);

        // 2. Resource exists and is available for booking
        var resource = await resourceRepo.GetWithCategoryAsync(resourceId);
        if (resource is null)
            return ServiceResult<Booking>.Fail("The selected resource does not exist.");

        if (resource.Status != ResourceStatus.Available)
            return ServiceResult<Booking>.Fail(
                $"\"{resource.Name}\" is currently {resource.Status.ToString().ToLowerInvariant()} and cannot be booked.");

        // 3. User has not exceeded their active booking limit
        var activeCount = await bookingRepo.GetActiveBookingCountAsync(userId);
        if (activeCount >= MaxActiveBookingsPerUser)
            return ServiceResult<Booking>.Fail(
                $"You already have {MaxActiveBookingsPerUser} active bookings. " +
                "Please cancel one before making a new reservation.");

        // 4. No overlap with existing bookings on this resource
        var hasOverlap = await bookingRepo.HasOverlapAsync(resourceId, start, end);
        if (hasOverlap)
            return ServiceResult<Booking>.Fail(
                "This resource is already booked for the selected time slot. " +
                "Please choose a different time.");

        // 5. Persist
        var booking = new Booking
        {
            UserId     = userId,
            ResourceId = resourceId,
            StartTime  = start,
            EndTime    = end,
            Purpose    = purpose?.Trim(),
            Status     = BookingStatus.Pending,
            CreatedAt  = DateTime.UtcNow
        };

        await bookingRepo.AddAsync(booking);
        await bookingRepo.SaveChangesAsync();

        logger.LogInformation(
            "Booking {BookingId} created by user {UserId} for resource {ResourceId} [{Start} – {End}]",
            booking.Id, userId, resourceId, start, end);

        // Load navigation data so the caller has a fully populated object
        var created = await bookingRepo.GetWithDetailsAsync(booking.Id);

        // Send notification (fire-and-forget — failure must not break booking)
        _ = SafeSendEmailAsync(() =>
            emailService.SendBookingCreatedAsync(
                created!.User.Email!, created.User.FullName, resource.Name, start, end));

        return ServiceResult<Booking>.Ok(created!);
    }

    // ── Cancel (by user) ─────────────────────────────────────────────────────

    public async Task<ServiceResult> CancelBookingAsync(int bookingId, string userId)
    {
        var booking = await bookingRepo.GetByIdAsync(bookingId);

        if (booking is null)
            return ServiceResult.Fail("Booking not found.");

        if (booking.UserId != userId)
            return ServiceResult.Fail("You can only cancel your own bookings.");

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Rejected)
            return ServiceResult.Fail("This booking is already cancelled or rejected.");

        if (booking.StartTime.HasValue && booking.StartTime.Value <= DateTime.UtcNow)
            return ServiceResult.Fail("You cannot cancel a booking that has already started or passed.");

        booking.Status      = BookingStatus.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;

        bookingRepo.Update(booking);
        await bookingRepo.SaveChangesAsync();

        logger.LogInformation("Booking {BookingId} cancelled by user {UserId}", bookingId, userId);

        // Reload with navigation for email
        var full = await bookingRepo.GetWithDetailsAsync(bookingId);
        if (full?.User?.Email != null && full.StartTime.HasValue && full.EndTime.HasValue)
        {
            _ = SafeSendEmailAsync(() =>
                emailService.SendBookingCancelledAsync(
                    full.User.Email, full.User.FullName,
                    full.Resource?.Name ?? "Unknown",
                    full.StartTime.Value, full.EndTime.Value));
        }

        return ServiceResult.Ok();
    }

    // ── Admin: Confirm ────────────────────────────────────────────────────────

    public async Task<ServiceResult> ConfirmBookingAsync(int bookingId, string? adminNote)
    {
        var booking = await bookingRepo.GetByIdAsync(bookingId);

        if (booking is null)
            return ServiceResult.Fail("Booking not found.");

        if (booking.Status != BookingStatus.Pending)
            return ServiceResult.Fail(
                $"Only Pending bookings can be confirmed. Current status: {booking.Status}.");

        booking.Status    = BookingStatus.Confirmed;
        booking.AdminNote = adminNote?.Trim();

        bookingRepo.Update(booking);
        await bookingRepo.SaveChangesAsync();

        logger.LogInformation("Booking {BookingId} confirmed by admin", bookingId);

        var full = await bookingRepo.GetWithDetailsAsync(bookingId);
        if (full?.User?.Email != null && full.StartTime.HasValue && full.EndTime.HasValue)
        {
            _ = SafeSendEmailAsync(() =>
                emailService.SendBookingConfirmedAsync(
                    full.User.Email, full.User.FullName,
                    full.Resource?.Name ?? "Unknown",
                    full.StartTime.Value, full.EndTime.Value));
        }

        return ServiceResult.Ok();
    }

    // ── Admin: Reject ─────────────────────────────────────────────────────────

    public async Task<ServiceResult> RejectBookingAsync(int bookingId, string adminNote)
    {
        if (string.IsNullOrWhiteSpace(adminNote))
            return ServiceResult.Fail("A rejection reason is required.");

        var booking = await bookingRepo.GetByIdAsync(bookingId);

        if (booking is null)
            return ServiceResult.Fail("Booking not found.");

        if (booking.Status != BookingStatus.Pending)
            return ServiceResult.Fail(
                $"Only Pending bookings can be rejected. Current status: {booking.Status}.");

        booking.Status    = BookingStatus.Rejected;
        booking.AdminNote = adminNote.Trim();

        bookingRepo.Update(booking);
        await bookingRepo.SaveChangesAsync();

        logger.LogInformation("Booking {BookingId} rejected by admin. Reason: {Note}", bookingId, adminNote);

        var full = await bookingRepo.GetWithDetailsAsync(bookingId);
        if (full?.User?.Email != null)
        {
            _ = SafeSendEmailAsync(() =>
                emailService.SendBookingRejectedAsync(
                    full.User.Email, full.User.FullName,
                    full.Resource?.Name ?? "Unknown",
                    adminNote.Trim()));
        }

        return ServiceResult.Ok();
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public Task<IEnumerable<Booking>> GetUserBookingsAsync(string userId) =>
        bookingRepo.GetUserBookingsAsync(userId);

    public Task<IEnumerable<Booking>> GetAllBookingsAsync() =>
        bookingRepo.GetAllWithDetailsAsync();

    public Task<Booking?> GetBookingByIdAsync(int bookingId) =>
        bookingRepo.GetWithDetailsAsync(bookingId);

    // ── Private helpers ───────────────────────────────────────────────────────

    private static ServiceResult ValidateTimes(DateTime start, DateTime end)
    {
        var now = DateTime.UtcNow;

        if (start <= now)
            return ServiceResult.Fail("The start time must be in the future.");

        if (end <= start)
            return ServiceResult.Fail("The end time must be after the start time.");

        var duration = end - start;

        if (duration.TotalMinutes < MinDurationMinutes)
            return ServiceResult.Fail(
                $"Bookings must be at least {MinDurationMinutes} minutes long.");

        if (duration.TotalHours > MaxDurationHours)
            return ServiceResult.Fail(
                $"Bookings cannot exceed {MaxDurationHours} hours.");

        if (start > now.AddDays(MaxAdvanceBookingDays))
            return ServiceResult.Fail(
                $"Bookings cannot be made more than {MaxAdvanceBookingDays} days in advance.");

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Fire-and-forget email wrapper. Logs failures instead of crashing the booking flow.
    /// </summary>
    private async Task SafeSendEmailAsync(Func<Task> sendAction)
    {
        try
        {
            await sendAction();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send email notification — booking was still saved.");
        }
    }
}
