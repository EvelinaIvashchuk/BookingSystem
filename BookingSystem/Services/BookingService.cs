using BookingSystem.Data;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services.Dtos;
using BookingSystem.Services.Interfaces;

namespace BookingSystem.Services;

/// <summary>
/// Реалізація IBookingService.
/// Використовує IUnitOfWork для доступу до репозиторіїв і фіксації змін,
/// що забезпечує атомарність операцій над кількома сутностями.
/// </summary>
public class BookingService(
    IUnitOfWork             uow,
    IEmailService           emailService,
    ILogger<BookingService> logger) : IBookingService
{
    // ── Бізнес-константи ──────────────────────────────────────────────────────
    private const int MaxActiveBookingsPerUser = 3;
    private const int MinDurationMinutes       = 30;
    private const int MaxDurationHours         = 8;
    private const int MaxAdvanceBookingDays    = 30;

    // ── Create ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ServiceResult<Booking>> CreateBookingAsync(
        string userId, CreateBookingDto dto)
    {
        // 1. Базова перевірка часів
        var timeCheck = ValidateTimes(dto.StartTime, dto.EndTime);
        if (!timeCheck.IsSuccess)
            return ServiceResult<Booking>.Fail(timeCheck.Error!);

        // 2. Ресурс існує і доступний для бронювання
        var resource = await uow.Resources.GetWithCategoryAsync(dto.ResourceId);
        if (resource is null)
            return ServiceResult<Booking>.Fail("The selected resource does not exist.");

        if (resource.Status != ResourceStatus.Available)
            return ServiceResult<Booking>.Fail(
                $"\"{resource.Name}\" is currently {resource.Status.ToString().ToLowerInvariant()} and cannot be booked.");

        // 3. Користувач не перевищив ліміт активних бронювань
        var activeCount = await uow.Bookings.GetActiveBookingCountAsync(userId);
        if (activeCount >= MaxActiveBookingsPerUser)
            return ServiceResult<Booking>.Fail(
                $"You already have {MaxActiveBookingsPerUser} active bookings. " +
                "Please cancel one before making a new reservation.");

        // 4. Немає накладань з іншими бронюваннями цього ресурсу
        var hasOverlap = await uow.Bookings.HasOverlapAsync(dto.ResourceId, dto.StartTime, dto.EndTime);
        if (hasOverlap)
            return ServiceResult<Booking>.Fail(
                "This resource is already booked for the selected time slot. " +
                "Please choose a different time.");

        // 5. Зберегти через UnitOfWork (єдина точка CommitAsync)
        var booking = new Booking
        {
            UserId     = userId,
            ResourceId = dto.ResourceId,
            StartTime  = dto.StartTime,
            EndTime    = dto.EndTime,
            Purpose    = dto.Purpose?.Trim(),
            Status     = BookingStatus.Pending,
            CreatedAt  = DateTime.UtcNow
        };

        await uow.Bookings.AddAsync(booking);
        await uow.CommitAsync();

        logger.LogInformation(
            "Booking {BookingId} created by user {UserId} for resource {ResourceId} [{Start} – {End}]",
            booking.Id, userId, dto.ResourceId, dto.StartTime, dto.EndTime);

        var created = await uow.Bookings.GetWithDetailsAsync(booking.Id);

        _ = SafeSendEmailAsync(() =>
            emailService.SendBookingCreatedAsync(
                created!.User.Email!, created.User.FullName,
                resource.Name, dto.StartTime, dto.EndTime));

        return ServiceResult<Booking>.Ok(created!);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ServiceResult> CancelBookingAsync(int bookingId, string userId)
    {
        var booking = await uow.Bookings.GetByIdAsync(bookingId);

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

        uow.Bookings.Update(booking);
        await uow.CommitAsync();

        logger.LogInformation("Booking {BookingId} cancelled by user {UserId}", bookingId, userId);

        var full = await uow.Bookings.GetWithDetailsAsync(bookingId);
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

    /// <inheritdoc/>
    public async Task<ServiceResult> ConfirmBookingAsync(int bookingId, string? adminNote)
    {
        var booking = await uow.Bookings.GetByIdAsync(bookingId);

        if (booking is null)
            return ServiceResult.Fail("Booking not found.");

        if (booking.Status != BookingStatus.Pending)
            return ServiceResult.Fail(
                $"Only Pending bookings can be confirmed. Current status: {booking.Status}.");

        booking.Status    = BookingStatus.Confirmed;
        booking.AdminNote = adminNote?.Trim();

        uow.Bookings.Update(booking);
        await uow.CommitAsync();

        logger.LogInformation("Booking {BookingId} confirmed by admin", bookingId);

        var full = await uow.Bookings.GetWithDetailsAsync(bookingId);
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

    /// <inheritdoc/>
    public async Task<ServiceResult> RejectBookingAsync(int bookingId, string adminNote)
    {
        if (string.IsNullOrWhiteSpace(adminNote))
            return ServiceResult.Fail("A rejection reason is required.");

        var booking = await uow.Bookings.GetByIdAsync(bookingId);

        if (booking is null)
            return ServiceResult.Fail("Booking not found.");

        if (booking.Status != BookingStatus.Pending)
            return ServiceResult.Fail(
                $"Only Pending bookings can be rejected. Current status: {booking.Status}.");

        booking.Status    = BookingStatus.Rejected;
        booking.AdminNote = adminNote.Trim();

        uow.Bookings.Update(booking);
        await uow.CommitAsync();

        logger.LogInformation("Booking {BookingId} rejected. Reason: {Note}", bookingId, adminNote);

        var full = await uow.Bookings.GetWithDetailsAsync(bookingId);
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

    /// <inheritdoc/>
    public Task<IEnumerable<Booking>> GetUserBookingsAsync(string userId) =>
        uow.Bookings.GetUserBookingsAsync(userId);

    /// <inheritdoc/>
    public Task<IEnumerable<Booking>> GetAllBookingsAsync() =>
        uow.Bookings.GetAllWithDetailsAsync();

    /// <inheritdoc/>
    public Task<Booking?> GetBookingByIdAsync(int bookingId) =>
        uow.Bookings.GetWithDetailsAsync(bookingId);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ServiceResult ValidateTimes(DateTime start, DateTime end)
    {
        var now = DateTime.UtcNow;

        if (start <= now)
            return ServiceResult.Fail("The start time must be in the future.");

        if (end <= start)
            return ServiceResult.Fail("The end time must be after the start time.");

        var duration = end - start;

        if (duration.TotalMinutes < MinDurationMinutes)
            return ServiceResult.Fail($"Bookings must be at least {MinDurationMinutes} minutes long.");

        if (duration.TotalHours > MaxDurationHours)
            return ServiceResult.Fail($"Bookings cannot exceed {MaxDurationHours} hours.");

        if (start > now.AddDays(MaxAdvanceBookingDays))
            return ServiceResult.Fail(
                $"Bookings cannot be made more than {MaxAdvanceBookingDays} days in advance.");

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Fire-and-forget обгортка. Логує помилки замість переривання потоку.
    /// </summary>
    private async Task SafeSendEmailAsync(Func<Task> sendAction)
    {
        try   { await sendAction(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Email notification failed — booking was still saved.");
        }
    }
}
