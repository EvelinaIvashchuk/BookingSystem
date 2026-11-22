using BookingSystem.Enums;

namespace BookingSystem.Data.Repositories;

/// <summary>Sort order of the car catalog.</summary>
public enum CarSort
{
    /// <summary>By category, then brand and model.</summary>
    Default = 0,
    PriceAsc = 1,
    PriceDesc = 2,
    Newest = 3
}

/// <summary>
/// Filters of the car catalog page. Every filter is optional; only cars with status Available are listed.
/// When both dates are set, cars with an active rental overlapping the period are excluded.
/// </summary>
public sealed record CarCatalogQuery
{
    /// <summary>Words to look for in brand, model, description and location; every word must match.</summary>
    public string? Search { get; init; }
    public int? CategoryId { get; init; }

    /// <summary>City, the part of <c>Car.Location</c> before the first comma.</summary>
    public string? City { get; init; }

    public DateTime? PickupDate { get; init; }
    public DateTime? ReturnDate { get; init; }
    public Transmission? Transmission { get; init; }
    public FuelType? FuelType { get; init; }
    public int? MinSeats { get; init; }
    public decimal? MaxPricePerDay { get; init; }
    public CarSort Sort { get; init; }
}

/// <summary>One page of results plus the total count; <see cref="Page"/> is the page actually returned.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page);
