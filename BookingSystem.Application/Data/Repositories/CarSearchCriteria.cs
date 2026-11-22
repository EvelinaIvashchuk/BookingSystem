using BookingSystem.Enums;

namespace BookingSystem.Data.Repositories;

/// <summary>Filters for searching cars that are free for a date range.</summary>
public sealed record CarSearchCriteria(DateTime PickupDate, DateTime ReturnDate)
{
    public int? CategoryId { get; init; }
    public Transmission? Transmission { get; init; }
    public FuelType? FuelType { get; init; }
    public int? MinSeats { get; init; }
    public decimal? MaxPricePerDay { get; init; }
}
