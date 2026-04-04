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

namespace BookingSystem.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController(
    IBookingService  bookingService,
    IResourceService resourceService,
    IUserService     userService,
    ApplicationDbContext db) : Controller   // DbContext used only for Category lookups
{
    // ── Dashboard ─────────────────────────────────────────────────────────────

    // GET /Admin
    public async Task<IActionResult> Index()
    {
        var allBookings  = (await bookingService.GetAllBookingsAsync()).ToList();
        var allResources = (await resourceService.GetAllResourcesAsync()).ToList();
        var today        = DateTime.UtcNow.Date;

        var vm = new AdminDashboardViewModel
        {
            TotalResources     = allResources.Count,
            AvailableResources = allResources.Count(r => r.Status == ResourceStatus.Available),
            TotalBookings      = allBookings.Count,
            PendingBookings    = allBookings.Count(b => b.Status == BookingStatus.Pending),
            TodaysBookings     = allBookings.Count(b =>
                b.StartTime.HasValue && b.StartTime.Value.Date == today),
            RecentPending      = allBookings
                .Where(b => b.Status == BookingStatus.Pending)
                .Take(10)
        };

        return View(vm);
    }

    // ── Resource Management ───────────────────────────────────────────────────

    // GET /Admin/Resources
    public async Task<IActionResult> Resources()
    {
        var resources = await resourceService.GetAllResourcesAsync();
        return View(resources);
    }

    // GET /Admin/CreateResource
    public async Task<IActionResult> CreateResource()
    {
        var vm = new ResourceFormViewModel
        {
            Categories = await GetCategorySelectListAsync()
        };
        return View(vm);
    }

    // POST /Admin/CreateResource
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateResource(ResourceFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Categories = await GetCategorySelectListAsync();
            return View(vm);
        }

        var resource = new Resource
        {
            Name        = vm.Name.Trim(),
            Description = vm.Description?.Trim(),
            Location    = vm.Location.Trim(),
            Capacity    = vm.Capacity,
            CategoryId  = vm.CategoryId,
            ImageUrl    = vm.ImageUrl?.Trim()
        };

        var result = await resourceService.CreateResourceAsync(resource);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error);
            vm.Categories = await GetCategorySelectListAsync();
            return View(vm);
        }

        TempData["Success"] = $"Resource \"{result.Value!.Name}\" created successfully.";
        return RedirectToAction(nameof(Resources));
    }

    // GET /Admin/EditResource/5
    public async Task<IActionResult> EditResource(int id)
    {
        var resource = await resourceService.GetResourceByIdAsync(id);
        if (resource is null) return NotFound();

        var vm = new ResourceFormViewModel
        {
            Id          = resource.Id,
            Name        = resource.Name,
            Description = resource.Description,
            Location    = resource.Location,
            Capacity    = resource.Capacity,
            CategoryId  = resource.CategoryId,
            ImageUrl    = resource.ImageUrl,
            Status      = resource.Status,
            Categories  = await GetCategorySelectListAsync(resource.CategoryId)
        };

        return View(vm);
    }

    // POST /Admin/EditResource
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditResource(ResourceFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Categories = await GetCategorySelectListAsync(vm.CategoryId);
            return View(vm);
        }

        var resource = new Resource
        {
            Id          = vm.Id,
            Name        = vm.Name.Trim(),
            Description = vm.Description?.Trim(),
            Location    = vm.Location.Trim(),
            Capacity    = vm.Capacity,
            CategoryId  = vm.CategoryId,
            ImageUrl    = vm.ImageUrl?.Trim(),
            Status      = vm.Status
        };

        var result = await resourceService.UpdateResourceAsync(resource);

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.Error);
            vm.Categories = await GetCategorySelectListAsync(vm.CategoryId);
            return View(vm);
        }

        TempData["Success"] = "Resource updated successfully.";
        return RedirectToAction(nameof(Resources));
    }

    // POST /Admin/SetResourceStatus
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetResourceStatus(int resourceId, ResourceStatus status)
    {
        var result = await resourceService.SetResourceStatusAsync(resourceId, status);

        if (result.IsSuccess)
            TempData["Success"] = "Resource status updated.";
        else
            TempData["Error"] = result.Error;

        return RedirectToAction(nameof(Resources));
    }

    // ── Booking Management ────────────────────────────────────────────────────

    // GET /Admin/Bookings?status=Pending&page=2
    public async Task<IActionResult> Bookings(BookingStatus? status, int page = 1)
    {
        var bookings = await bookingService.GetAllBookingsAsync();

        if (status.HasValue)
            bookings = bookings.Where(b => b.Status == status.Value);

        ViewBag.FilterStatus = status;
        var paged = PaginatedList<Booking>.Create(bookings, page, 15);
        return View(paged);
    }

    // POST /Admin/ConfirmBooking/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmBooking(int id, string? adminNote)
    {
        var result = await bookingService.ConfirmBookingAsync(id, adminNote);

        if (result.IsSuccess)
            TempData["Success"] = "Booking confirmed.";
        else
            TempData["Error"] = result.Error;

        return RedirectToAction(nameof(Bookings));
    }

    // GET /Admin/RejectBooking/5
    public async Task<IActionResult> RejectBooking(int id)
    {
        var booking = await bookingService.GetBookingByIdAsync(id);
        if (booking is null) return NotFound();

        if (booking.Status != BookingStatus.Pending)
        {
            TempData["Error"] = "Only Pending bookings can be rejected.";
            return RedirectToAction(nameof(Bookings));
        }

        var vm = new AdminRejectViewModel
        {
            BookingId    = booking.Id,
            ResourceName = booking.Resource?.Name ?? string.Empty,
            UserFullName = booking.User?.FullName ?? booking.UserId,
            TimeSlot     = booking.StartTime.HasValue && booking.EndTime.HasValue
                ? $"{booking.StartTime.Value:dd MMM yyyy HH:mm} – {booking.EndTime.Value:HH:mm}"
                : string.Empty
        };

        return View(vm);
    }

    // POST /Admin/RejectBooking
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectBooking(AdminRejectViewModel vm)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var result = await bookingService.RejectBookingAsync(vm.BookingId, vm.AdminNote);

        if (result.IsSuccess)
        {
            TempData["Success"] = "Booking rejected.";
            return RedirectToAction(nameof(Bookings));
        }

        ModelState.AddModelError(string.Empty, result.Error);
        return View(vm);
    }

    // ── User Management ───────────────────────────────────────────────────────

    // GET /Admin/Users
    public async Task<IActionResult> Users()
    {
        var usersWithRoles = await userService.GetAllUsersWithRolesAsync();
        return View(usersWithRoles);
    }

    // POST /Admin/ToggleUserActive
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUserActive(string userId, bool isActive)
    {
        var result = await userService.SetActiveStatusAsync(userId, isActive);

        if (result.IsSuccess)
            TempData["Success"] = isActive ? "User account activated." : "User account deactivated.";
        else
            TempData["Error"] = result.Error;

        return RedirectToAction(nameof(Users));
    }

    // POST /Admin/PromoteToAdmin
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PromoteToAdmin(string userId)
    {
        var result = await userService.PromoteToAdminAsync(userId);

        if (result.IsSuccess)
            TempData["Success"] = "User has been promoted to Admin.";
        else
            TempData["Error"] = result.Error;

        return RedirectToAction(nameof(Users));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
