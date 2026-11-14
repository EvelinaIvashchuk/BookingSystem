namespace BookingSystem.Services;

/// <summary>
/// Business rules of a rental shared by <see cref="RentalService"/> and the AI agent tools,
/// so that the agent never offers what booking would later reject.
/// </summary>
public static class RentalRules
{
    public const int MaxActiveRentalsPerUser = 3;
    public const int MinRentalDays = 1;
    public const int MaxRentalDays = 30;
    public const int MaxAdvanceBookingDays = 60;

    /// <summary>Number of rental days (calendar days between pickup and return).</summary>
    public static int CountDays(DateTime pickupDate, DateTime returnDate) =>
        (int)(returnDate.Date - pickupDate.Date).TotalDays;

    public static decimal CalculateTotalPrice(decimal pricePerDay, DateTime pickupDate, DateTime returnDate) =>
        CountDays(pickupDate, returnDate) * pricePerDay;

    /// <param name="today">Reference date for "past" and "in advance" checks.</param>
    public static ServiceResult ValidateDates(DateTime pickupDate, DateTime returnDate, DateTime today)
    {
        today = today.Date;

        if (pickupDate.Date < today)
            return ServiceResult.Fail("The pickup date cannot be in the past.");

        if (returnDate.Date <= pickupDate.Date)
            return ServiceResult.Fail("The return date must be after the pickup date.");

        var days = CountDays(pickupDate, returnDate);

        if (days < MinRentalDays)
            return ServiceResult.Fail($"Rental must be at least {MinRentalDays} day(s).");

        if (days > MaxRentalDays)
            return ServiceResult.Fail($"Rental cannot exceed {MaxRentalDays} days.");

        if (pickupDate.Date > today.AddDays(MaxAdvanceBookingDays))
            return ServiceResult.Fail(
                $"Rental cannot be booked more than {MaxAdvanceBookingDays} days in advance.");

        return ServiceResult.Ok();
    }
}
