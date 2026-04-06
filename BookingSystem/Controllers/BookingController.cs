using System.Security.Claims;
using AutoMapper;
using BookingSystem.Helpers;
using BookingSystem.Models;
using BookingSystem.Services.Dtos;
using BookingSystem.Services.Interfaces;
using BookingSystem.ViewModels;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingSystem.Controllers;

/// <summary>
/// Контролер для управління бронюваннями.
/// Використовує IMapper для перетворення ViewModel → DTO
/// та IValidator для валідації форми перед передачею в сервіс.
/// </summary>
[Authorize]
public class BookingController(
    IBookingService                    bookingService,
    IResourceService                   resourceService,
    IMapper                            mapper,
    IValidator<BookingCreateViewModel> validator) : Controller
{
    // ── Create ────────────────────────────────────────────────────────────────

    // GET /Booking/Create?resourceId=5
    public async Task<IActionResult> Create(int resourceId)
    {
        var resource = await resourceService.GetResourceByIdAsync(resourceId);

        if (resource is null)
            return NotFound();

        if (resource.Status != Enums.ResourceStatus.Available)
        {
            TempData["Error"] = $"\"{resource.Name}\" is not available for booking.";
            return RedirectToAction("Details", "Resource", new { id = resourceId });
        }

        var vm = new BookingCreateViewModel
        {
            ResourceId   = resource.Id,
            ResourceName = resource.Name,
            Location     = resource.Location,
            Capacity     = resource.Capacity,
            CategoryName = resource.Category?.Name ?? string.Empty
        };

        return View(vm);
    }

    // POST /Booking/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BookingCreateViewModel vm)
    {
        // FluentValidation — перевіряє правила з BookingCreateViewModelValidator
        var validationResult = await validator.ValidateAsync(vm);
        if (!validationResult.IsValid)
        {
            validationResult.AddToModelState(ModelState, null);
            await RepopulateDisplayFields(vm);
            return View(vm);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        // AutoMapper: ViewModel → DTO (відокремлює сервіс від MVC-шару)
        var dto = mapper.Map<CreateBookingDto>(vm);

        var result = await bookingService.CreateBookingAsync(userId, dto);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await RepopulateDisplayFields(vm);
            return View(vm);
        }

        TempData["Success"] = "Your booking has been submitted and is pending confirmation.";
        return RedirectToAction(nameof(MyBookings));
    }

    // ── My Bookings ───────────────────────────────────────────────────────────

    // GET /Booking/MyBookings?page=2
    public async Task<IActionResult> MyBookings(int page = 1)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var bookings = await bookingService.GetUserBookingsAsync(userId);
        var paged    = PaginatedList<Booking>.Create(bookings, page, 10);
        return View(paged);
    }

    // ── Details ───────────────────────────────────────────────────────────────

    // GET /Booking/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var booking = await bookingService.GetBookingByIdAsync(id);

        if (booking is null)
            return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (booking.UserId != userId && !User.IsInRole("Admin"))
            return Forbid();

        return View(booking);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    // POST /Booking/Cancel/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var result = await bookingService.CancelBookingAsync(id, userId);

        if (result.IsSuccess)
            TempData["Success"] = "Your booking has been cancelled.";
        else
            TempData["Error"] = result.Error;

        return RedirectToAction(nameof(MyBookings));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Повторно заповнює display-only поля ViewModel після невдалого POST.
    /// </summary>
    private async Task RepopulateDisplayFields(BookingCreateViewModel vm)
    {
        var resource = await resourceService.GetResourceByIdAsync(vm.ResourceId);
        if (resource is not null)
        {
            vm.ResourceName = resource.Name;
            vm.Location     = resource.Location;
            vm.Capacity     = resource.Capacity;
            vm.CategoryName = resource.Category?.Name ?? string.Empty;
        }
    }
}
