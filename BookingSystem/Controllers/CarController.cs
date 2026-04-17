using BookingSystem.Data;
using BookingSystem.Helpers;
using BookingSystem.Models;
using BookingSystem.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BookingSystem.Controllers;

public class CarController(ICarService carService, IUnitOfWork uow) : Controller
{
    private const int PageSize = 6;

    // GET /Car?search=toyota&categoryId=1&page=2
    public async Task<IActionResult> Index(string? search, int? categoryId, int page = 1)
    {
        var cars = await carService.GetAvailableCarsAsync();

        if (categoryId.HasValue)
            cars = cars.Where(c => c.CategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(search))
            cars = cars.Where(c =>
                c.Brand.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.Model.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (c.Description != null &&
                 c.Description.Contains(search, StringComparison.OrdinalIgnoreCase)));

        var paged = PaginatedList<Car>.Create(cars, page, PageSize);

        ViewBag.Search     = search;
        ViewBag.CategoryId = categoryId;
        ViewBag.Categories = (await carService.GetAllCategoriesAsync())
            .Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name,
                Selected = c.Id == categoryId
            });

        return View(paged);
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
