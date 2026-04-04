using System.Security.Claims;
using BookingSystem.Helpers;
using BookingSystem.Models;
using BookingSystem.Services.Interfaces;
using BookingSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingSystem.Controllers;

[Authorize]  // All booking actions require a logged-in user
public class BookingController(
    IBookingService  bookingService,
    IResourceService resourceService) : Controller
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
        if (!ModelState.IsValid)
        {
            // Re-populate display-only fields lost on postback
            var resource = await resourceService.GetResourceByIdAsync(vm.ResourceId);
            if (resource is not null)
            {
                vm.ResourceName = resource.Name;
                vm.Location     = resource.Location;
                vm.Capacity     = resource.Capacity;
                vm.CategoryName = resource.Category?.Name ?? string.Empty;
            }
            return View(vm);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var result = await bookingService.CreateBookingAsync(
            userId,
            vm.ResourceId,
            vm.StartTime!.Value,   // validated non-null by [Required]
            vm.EndTime!.Value,
            vm.Purpose);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error);

            // Re-populate display-only fields
            var resource = await resourceService.GetResourceByIdAsync(vm.ResourceId);
            if (resource is not null)
            {
                vm.ResourceName = resource.Name;
                vm.Location     = resource.Location;
                vm.Capacity     = resource.Capacity;
                vm.CategoryName = resource.Category?.Name ?? string.Empty;
            }
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
        var paged = PaginatedList<Booking>.Create(bookings, page, 10);
        return View(paged);
    }

    // ── Details ───────────────────────────────────────────────────────────────

    // GET /Booking/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var booking = await bookingService.GetBookingByIdAsync(id);

        if (booking is null)
            return NotFound();

        // Users can only view their own bookings; admins can view all
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
}
