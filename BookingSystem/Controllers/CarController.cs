using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Helpers;
using BookingSystem.Models;
using BookingSystem.Services;
using BookingSystem.Services.Interfaces;
using BookingSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace BookingSystem.Controllers;

public class CarController(ICarService carService, IUnitOfWork uow) : Controller
{
    private const int PageSize = 6;

    // GET /Car?search=toyota&city=Київ&pickupDate=2026-12-01&returnDate=2026-12-04&transmission=Automatic&sort=PriceAsc&page=2
    public async Task<IActionResult> Index([FromQuery] CarCatalogFilter? filter)
    {
        filter ??= new CarCatalogFilter();
        var today = DateTime.UtcNow.Date;
        var (pickup, ret, dateError) = ResolvePeriod(filter.PickupDate, filter.ReturnDate, today);

        var query = new CarCatalogQuery
        {
            Search         = filter.Search,
            CategoryId     = filter.CategoryId,
            City           = filter.City,
            PickupDate     = pickup,
            ReturnDate     = ret,
            Transmission   = filter.Transmission,
            FuelType       = filter.FuelType,
            MinSeats       = filter.MinSeats,
            MaxPricePerDay = filter.MaxPrice,
            Sort           = filter.Sort
        };

        var result = await carService.SearchCatalogAsync(query, filter.Page, PageSize);

        return View(new CarCatalogViewModel
        {
            Filter       = filter,
            Cars         = new PaginatedList<Car>(result.Items, result.TotalCount, result.Page, PageSize),
            Categories   = (await carService.GetAllCategoriesAsync()).ToList(),
            Cities       = await carService.GetCitiesAsync(),
            Today        = today,
            RentalDays   = pickup.HasValue && ret.HasValue ? RentalRules.CountDays(pickup.Value, ret.Value) : null,
            DateErrorKey = dateError
        });
    }

    /// <summary>
    /// The period to filter by, or the reason it is ignored: both dates are needed and they must pass
    /// the same rules as booking, so every car found can actually be booked for those dates.
    /// </summary>
    public static (DateTime? Pickup, DateTime? Return, string? ErrorKey) ResolvePeriod(
        DateTime? pickupDate, DateTime? returnDate, DateTime today)
    {
        if (pickupDate is null && returnDate is null)
            return (null, null, null);

        if (pickupDate is null || returnDate is null)
            return (null, null, "Car_DatesBoth");

        return RentalRules.ValidateDates(pickupDate.Value, returnDate.Value, today).IsSuccess
            ? (pickupDate.Value.Date, returnDate.Value.Date, null)
            : (null, null, "Car_DatesInvalid");
    }

    // GET /Car/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var car = await carService.GetCarByIdAsync(id);

        if (car is null)
            return NotFound();

        return View(car);
    }

    // GET /Car/BookedRanges/5  →  JSON for the availability calendar
    [HttpGet]
    public async Task<IActionResult> BookedRanges(int id)
    {
        var ranges = await uow.Rentals.GetBookedRangesForCarAsync(id);
        var result = ranges.Select(r => new
        {
            pickup = r.Pickup.ToString("yyyy-MM-dd"),
            @return = r.Return.ToString("yyyy-MM-dd")
        });
        return Json(result);
    }
}
