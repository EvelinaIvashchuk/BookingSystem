using System.Security.Claims;
using AutoMapper;
using BookingSystem.Resources;
using BookingSystem.Helpers;
using BookingSystem.Models;
using BookingSystem.Services.Dtos;
using BookingSystem.Services.Interfaces;
using BookingSystem.ViewModels;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BookingSystem.Controllers;

[Authorize]
public class RentalController(IRentalService rentalService, ICarService carService, IMapper mapper,
    IValidator<RentalCreateViewModel> validator, IStringLocalizer<SharedResources> localizer) : Controller
{
    // GET /Rental/Create?carId=5&pickupDate=2026-05-01&returnDate=2026-05-05
    public async Task<IActionResult> Create(int carId, DateTime? pickupDate, DateTime? returnDate)
    {
        var car = await carService.GetCarByIdAsync(carId);

        if (car is null)
            return NotFound();

        if (car.Status != Enums.CarStatus.Available)
        {
            TempData["Error"] = string.Format(localizer["Msg_CarNotAvailable"].Value, car.FullName);
            return RedirectToAction("Details", "Car", new { id = carId });
        }

        var vm = new RentalCreateViewModel
        {
            CarId        = car.Id,
            CarName      = car.FullName,
            Location     = car.Location,
            PricePerDay  = car.PricePerDay,
            CategoryName = car.Category?.Name ?? string.Empty,
            PickupDate   = pickupDate,
            ReturnDate   = returnDate
        };

        return View(vm);
    }

    // POST /Rental/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RentalCreateViewModel vm)
    {
        var validationResult = await validator.ValidateAsync(vm);
        if (!validationResult.IsValid)
        {
            validationResult.AddToModelState(ModelState, null);
            await RepopulateDisplayFields(vm);
            return View(vm);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var dto = mapper.Map<CreateRentalDto>(vm);

        var result = await rentalService.CreateRentalAsync(userId, dto);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error);
            await RepopulateDisplayFields(vm);
            return View(vm);
        }

        TempData["Success"] = localizer["Msg_RentalSubmitted"].Value;
        return RedirectToAction(nameof(MyRentals));
    }

    // GET /Rental/MyRentals?page=2
    public async Task<IActionResult> MyRentals(int page = 1)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var rentals = await rentalService.GetUserRentalsAsync(userId);
        var paged   = PaginatedList<Rental>.Create(rentals, page, 10);
        return View(paged);
    }

    // GET /Rental/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var rental = await rentalService.GetRentalByIdAsync(id);

        if (rental is null)
            return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (rental.UserId != userId && !User.IsInRole("Admin"))
            return Forbid();

        return View(rental);
    }

    // POST /Rental/Cancel/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var result = await rentalService.CancelRentalAsync(id, userId);

        if (result.IsSuccess)
            TempData["Success"] = localizer["Msg_RentalCancelled"].Value;
        else
            TempData["Error"] = result.Error;

        return RedirectToAction(nameof(MyRentals));
    }

    private async Task RepopulateDisplayFields(RentalCreateViewModel vm)
    {
        var car = await carService.GetCarByIdAsync(vm.CarId);
        if (car is not null)
        {
            vm.CarName = car.FullName;
            vm.Location = car.Location;
            vm.PricePerDay = car.PricePerDay;
            vm.CategoryName = car.Category?.Name ?? string.Empty;
        }
    }
}
