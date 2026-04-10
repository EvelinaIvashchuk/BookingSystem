using BookingSystem;
using BookingSystem.Resources;
using BookingSystem.Data;
using BookingSystem.Enums;
using BookingSystem.Helpers;
using BookingSystem.Models;
using BookingSystem.Services.Interfaces;
using BookingSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace BookingSystem.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController(
    IRentalService                    rentalService,
    ICarService                       carService,
    IUserService                      userService,
    ApplicationDbContext              db,
    IStringLocalizer<SharedResources> localizer) : Controller
{
    // ── Dashboard ─────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        var allRentals = (await rentalService.GetAllRentalsAsync()).ToList();
        var allCars    = (await carService.GetAllCarsAsync()).ToList();
        var today      = DateTime.UtcNow.Date;

        var vm = new AdminDashboardViewModel
        {
            TotalCars      = allCars.Count,
            AvailableCars  = allCars.Count(c => c.Status == CarStatus.Available),
            TotalRentals   = allRentals.Count,
            PendingRentals = allRentals.Count(r => r.Status == RentalStatus.Pending),
            TodaysRentals  = allRentals.Count(r =>
                r.PickupDate.HasValue && r.PickupDate.Value.Date == today),
            RecentPending  = allRentals
                .Where(r => r.Status == RentalStatus.Pending)
                .Take(10)
        };

        return View(vm);
    }

    // ── Car Management ────────────────────────────────────────────────────────

    public async Task<IActionResult> Cars()
    {
        var cars = await carService.GetAllCarsAsync();
        return View(cars);
    }

    public async Task<IActionResult> CreateCar()
    {
        var vm = new CarFormViewModel
        {
            Categories = await GetCategorySelectListAsync()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCar(CarFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Categories = await GetCategorySelectListAsync();
            return View(vm);
        }

        var car = new Car
        {
            Brand        = vm.Brand.Trim(),
            Model        = vm.Model.Trim(),
            Year         = vm.Year,
            LicensePlate = vm.LicensePlate.Trim().ToUpperInvariant(),
            FuelType     = vm.FuelType,
            Transmission = vm.Transmission,
            Seats        = vm.Seats,
            PricePerDay  = vm.PricePerDay,
            Description  = vm.Description?.Trim(),
            Location     = vm.Location.Trim(),
            CategoryId   = vm.CategoryId,
            ImageUrl     = vm.ImageUrl?.Trim()
        };

        var result = await carService.CreateCarAsync(car);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error);
            vm.Categories = await GetCategorySelectListAsync();
            return View(vm);
        }

        TempData["Success"] = string.Format(localizer["Msg_CarCreated"].Value, result.Value!.FullName);
        return RedirectToAction(nameof(Cars));
    }

    public async Task<IActionResult> EditCar(int id)
    {
        var car = await carService.GetCarByIdAsync(id);
        if (car is null) return NotFound();

        var vm = new CarFormViewModel
        {
            Id           = car.Id,
            Brand        = car.Brand,
            Model        = car.Model,
            Year         = car.Year,
            LicensePlate = car.LicensePlate,
            FuelType     = car.FuelType,
            Transmission = car.Transmission,
            Seats        = car.Seats,
            PricePerDay  = car.PricePerDay,
            Description  = car.Description,
            Location     = car.Location,
            CategoryId   = car.CategoryId,
            ImageUrl     = car.ImageUrl,
            Status       = car.Status,
            Categories   = await GetCategorySelectListAsync(car.CategoryId)
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCar(CarFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Categories = await GetCategorySelectListAsync(vm.CategoryId);
            return View(vm);
        }

        var car = new Car
        {
            Id           = vm.Id,
            Brand        = vm.Brand.Trim(),
            Model        = vm.Model.Trim(),
            Year         = vm.Year,
            LicensePlate = vm.LicensePlate.Trim().ToUpperInvariant(),
            FuelType     = vm.FuelType,
            Transmission = vm.Transmission,
            Seats        = vm.Seats,
            PricePerDay  = vm.PricePerDay,
            Description  = vm.Description?.Trim(),
            Location     = vm.Location.Trim(),
            CategoryId   = vm.CategoryId,
            ImageUrl     = vm.ImageUrl?.Trim(),
            Status       = vm.Status
        };

        var result = await carService.UpdateCarAsync(car);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error);
            vm.Categories = await GetCategorySelectListAsync(vm.CategoryId);
            return View(vm);
        }

        TempData["Success"] = localizer["Msg_CarUpdated"].Value;
        return RedirectToAction(nameof(Cars));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCarStatus(int carId, CarStatus status)
    {
        var result = await carService.SetCarStatusAsync(carId, status);

        if (result.IsSuccess)
            TempData["Success"] = localizer["Msg_CarStatusUpdated"].Value;
        else
            TempData["Error"] = result.Error;

        return RedirectToAction(nameof(Cars));
    }

    // ── Rental Management ─────────────────────────────────────────────────────

    public async Task<IActionResult> Rentals(RentalStatus? status, int page = 1)
    {
        var rentals = await rentalService.GetAllRentalsAsync();

        if (status.HasValue)
            rentals = rentals.Where(r => r.Status == status.Value);

        ViewBag.FilterStatus = status;
        var paged = PaginatedList<Rental>.Create(rentals, page, 15);
        return View(paged);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmRental(int id, string? adminNote)
    {
        var result = await rentalService.ConfirmRentalAsync(id, adminNote);

        if (result.IsSuccess)
            TempData["Success"] = localizer["Msg_RentalConfirmed"].Value;
        else
            TempData["Error"] = result.Error;

        return RedirectToAction(nameof(Rentals));
    }

    public async Task<IActionResult> RejectRental(int id)
    {
        var rental = await rentalService.GetRentalByIdAsync(id);
        if (rental is null) return NotFound();

        if (rental.Status != RentalStatus.Pending)
        {
            TempData["Error"] = localizer["Msg_OnlyPendingRejected"].Value;
            return RedirectToAction(nameof(Rentals));
        }

        var vm = new AdminRejectViewModel
        {
            RentalId     = rental.Id,
            CarName      = rental.Car?.FullName ?? string.Empty,
            UserFullName = rental.User?.FullName ?? rental.UserId,
            DateRange    = rental.PickupDate.HasValue && rental.ReturnDate.HasValue
                ? $"{rental.PickupDate.Value:dd MMM yyyy} – {rental.ReturnDate.Value:dd MMM yyyy}"
                : string.Empty
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectRental(AdminRejectViewModel vm)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var result = await rentalService.RejectRentalAsync(vm.RentalId, vm.AdminNote);

        if (result.IsSuccess)
        {
            TempData["Success"] = localizer["Msg_RentalRejected"].Value;
            return RedirectToAction(nameof(Rentals));
        }

        ModelState.AddModelError(string.Empty, result.Error);
        return View(vm);
    }

    // ── User Management ───────────────────────────────────────────────────────

    public async Task<IActionResult> Users()
    {
        var usersWithRoles = await userService.GetAllUsersWithRolesAsync();
        return View(usersWithRoles);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUserActive(string userId, bool isActive)
    {
        var result = await userService.SetActiveStatusAsync(userId, isActive);

        if (result.IsSuccess)
            TempData["Success"] = isActive
                ? localizer["Msg_UserActivated"].Value
                : localizer["Msg_UserDeactivated"].Value;
        else
            TempData["Error"] = result.Error;

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PromoteToAdmin(string userId)
    {
        var result = await userService.PromoteToAdminAsync(userId);

        if (result.IsSuccess)
            TempData["Success"] = localizer["Msg_UserPromoted"].Value;
        else
            TempData["Error"] = result.Error;

        return RedirectToAction(nameof(Users));
    }

    private async Task<IEnumerable<SelectListItem>> GetCategorySelectListAsync(int? selectedId = null)
    {
        var categories = await db.Categories
            .OrderBy(c => c.Name)
            .ToListAsync();

        return categories.Select(c => new SelectListItem
        {
            Value    = c.Id.ToString(),
            Text     = c.Name,
            Selected = c.Id == selectedId
        });
    }
}
