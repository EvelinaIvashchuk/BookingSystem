using System.Globalization;
using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Helpers;
using BookingSystem.Models;

namespace BookingSystem.ViewModels;

/// <summary>Query-string filters of the car catalog (<c>GET /Car</c>).</summary>
public sealed class CarCatalogFilter
{
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public string? City { get; set; }
    public DateTime? PickupDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public Transmission? Transmission { get; set; }
    public FuelType? FuelType { get; set; }
    public int? MinSeats { get; set; }
    public decimal? MaxPrice { get; set; }
    public CarSort Sort { get; set; }
    public int Page { get; set; } = 1;

    /// <summary>True when anything differs from the unfiltered catalog (so "Reset" is worth showing).</summary>
    public bool IsFiltered => ToRouteValues().Count > 0;

    /// <summary>Query-string values of the filters that are set, without the page; links use them to keep the filters.</summary>
    public Dictionary<string, string> ToRouteValues()
    {
        var values = new Dictionary<string, string>();

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                values[key] = value.Trim();
        }

        Add("search", Search);
        Add("categoryId", CategoryId?.ToString(CultureInfo.InvariantCulture));
        Add("city", City);
        Add("pickupDate", PickupDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("returnDate", ReturnDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("transmission", Transmission?.ToString());
        Add("fuelType", FuelType?.ToString());
        Add("minSeats", MinSeats?.ToString(CultureInfo.InvariantCulture));
        Add("maxPrice", MaxPrice?.ToString(CultureInfo.InvariantCulture));
        if (Sort != CarSort.Default)
            Add("sort", Sort.ToString());

        return values;
    }
}

public sealed class CarCatalogViewModel
{
    public required CarCatalogFilter Filter { get; init; }
    public required PaginatedList<Car> Cars { get; init; }
    public IReadOnlyList<Category> Categories { get; init; } = [];
    public IReadOnlyList<string> Cities { get; init; } = [];

    /// <summary>Earliest pickup date the booking rules accept (today).</summary>
    public DateTime Today { get; init; }

    /// <summary>Number of rental days when the date filter was applied; null otherwise.</summary>
    public int? RentalDays { get; init; }

    /// <summary>Resource key of the message explaining why the dates were ignored.</summary>
    public string? DateErrorKey { get; init; }
}
