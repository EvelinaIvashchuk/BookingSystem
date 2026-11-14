using BookingSystem.Data;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services.Dtos;
using BookingSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BookingSystem.Services;

public class RentalService(IUnitOfWork uow, IEmailService emailService, ILogger<RentalService>  logger) : IRentalService
{
    public async Task<ServiceResult<Rental>> CreateRentalAsync(
        string userId, CreateRentalDto dto)
    {
        var dateCheck = RentalRules.ValidateDates(dto.PickupDate, dto.ReturnDate, DateTime.UtcNow.Date);
        if (!dateCheck.IsSuccess)
            return ServiceResult<Rental>.Fail(dateCheck.Error!);

        var car = await uow.Cars.GetWithCategoryAsync(dto.CarId);
        if (car is null)
            return ServiceResult<Rental>.Fail("The selected car does not exist.");

        if (car.Status != CarStatus.Available)
            return ServiceResult<Rental>.Fail(
                $"\"{car.FullName}\" is currently {car.Status.ToString().ToLowerInvariant()} and cannot be rented.");

        var activeCount = await uow.Rentals.GetActiveRentalCountAsync(userId);
        if (activeCount >= RentalRules.MaxActiveRentalsPerUser)
            return ServiceResult<Rental>.Fail(
                $"You already have {RentalRules.MaxActiveRentalsPerUser} active rentals. " +
                "Please cancel one before making a new reservation.");

        var hasOverlap = await uow.Rentals.HasOverlapAsync(dto.CarId, dto.PickupDate, dto.ReturnDate);
        if (hasOverlap)
            return ServiceResult<Rental>.Fail(
                "This car is already rented for the selected dates. " +
                "Please choose different dates.");

        var totalPrice = RentalRules.CalculateTotalPrice(car.PricePerDay, dto.PickupDate, dto.ReturnDate);

        var rental = new Rental
        {
            UserId = userId,
            CarId = dto.CarId,
            PickupDate = dto.PickupDate,
            ReturnDate = dto.ReturnDate,
            TotalPrice = totalPrice,
            Notes = dto.Notes?.Trim(),
            Status = RentalStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await uow.Rentals.AddAsync(rental);
        await uow.CommitAsync();

        logger.LogInformation(
            "Rental {RentalId} created by user {UserId} for car {CarId} [{Pickup} – {Return}]",
            rental.Id, userId, dto.CarId, dto.PickupDate, dto.ReturnDate);

        var created = await uow.Rentals.GetWithDetailsAsync(rental.Id);

        _ = SafeSendEmailAsync(() =>
            emailService.SendRentalCreatedAsync(
                created!.User.Email!, created.User.FullName,
                car.FullName, dto.PickupDate, dto.ReturnDate));

        return ServiceResult<Rental>.Ok(created!);
    }

    public async Task<ServiceResult> CancelRentalAsync(int rentalId, string userId)
    {
        var rental = await uow.Rentals.GetByIdAsync(rentalId);

        if (rental is null)
            return ServiceResult.Fail("Rental not found.");

        if (rental.UserId != userId)
            return ServiceResult.Fail("You can only cancel your own rentals.");

        if (rental.Status is RentalStatus.Cancelled or RentalStatus.Rejected)
            return ServiceResult.Fail("This rental is already cancelled or rejected.");

        if (rental.PickupDate.HasValue && rental.PickupDate.Value <= DateTime.UtcNow)
            return ServiceResult.Fail("You cannot cancel a rental that has already started or passed.");

        rental.Status      = RentalStatus.Cancelled;
        rental.CancelledAt = DateTime.UtcNow;

        uow.Rentals.Update(rental);
        await uow.CommitAsync();

        logger.LogInformation("Rental {RentalId} cancelled by user {UserId}", rentalId, userId);

        var full = await uow.Rentals.GetWithDetailsAsync(rentalId);
        if (full?.User?.Email != null && full.PickupDate.HasValue && full.ReturnDate.HasValue)
        {
            _ = SafeSendEmailAsync(() =>
                emailService.SendRentalCancelledAsync(
                    full.User.Email, full.User.FullName,
                    full.Car?.FullName ?? "Unknown",
                    full.PickupDate.Value, full.ReturnDate.Value));
        }

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ConfirmRentalAsync(int rentalId, string? adminNote)
    {
        var rental = await uow.Rentals.GetByIdAsync(rentalId);

        if (rental is null)
            return ServiceResult.Fail("Rental not found.");

        if (rental.Status != RentalStatus.Pending)
            return ServiceResult.Fail(
                $"Only Pending rentals can be confirmed. Current status: {rental.Status}.");

        rental.Status    = RentalStatus.Confirmed;
        rental.AdminNote = adminNote?.Trim();

        uow.Rentals.Update(rental);
        await uow.CommitAsync();

        logger.LogInformation("Rental {RentalId} confirmed by admin", rentalId);

        var full = await uow.Rentals.GetWithDetailsAsync(rentalId);
        if (full?.User?.Email != null && full.PickupDate.HasValue && full.ReturnDate.HasValue)
        {
            _ = SafeSendEmailAsync(() =>
                emailService.SendRentalConfirmedAsync(
                    full.User.Email, full.User.FullName,
                    full.Car?.FullName ?? "Unknown",
                    full.PickupDate.Value, full.ReturnDate.Value));
        }

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectRentalAsync(int rentalId, string adminNote)
    {
        if (string.IsNullOrWhiteSpace(adminNote))
            return ServiceResult.Fail("A rejection reason is required.");

        var rental = await uow.Rentals.GetByIdAsync(rentalId);

        if (rental is null)
            return ServiceResult.Fail("Rental not found.");

        if (rental.Status != RentalStatus.Pending)
            return ServiceResult.Fail(
                $"Only Pending rentals can be rejected. Current status: {rental.Status}.");

        rental.Status    = RentalStatus.Rejected;
        rental.AdminNote = adminNote.Trim();

        uow.Rentals.Update(rental);
        await uow.CommitAsync();

        logger.LogInformation("Rental {RentalId} rejected. Reason: {Note}", rentalId, adminNote);

        var full = await uow.Rentals.GetWithDetailsAsync(rentalId);
        if (full?.User?.Email != null)
        {
            _ = SafeSendEmailAsync(() =>
                emailService.SendRentalRejectedAsync(
                    full.User.Email, full.User.FullName,
                    full.Car?.FullName ?? "Unknown",
                    adminNote.Trim()));
        }

        return ServiceResult.Ok();
    }

    public Task<IEnumerable<Rental>> GetUserRentalsAsync(string userId) =>
        uow.Rentals.GetUserRentalsAsync(userId);

    public Task<IEnumerable<Rental>> GetAllRentalsAsync() =>
        uow.Rentals.GetAllWithDetailsAsync();

    public Task<Rental?> GetRentalByIdAsync(int rentalId) =>
        uow.Rentals.GetWithDetailsAsync(rentalId);


    private async Task SafeSendEmailAsync(Func<Task> sendAction)
    {
        try   { await sendAction(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Email notification failed — rental was still saved.");
        }
    }
}
