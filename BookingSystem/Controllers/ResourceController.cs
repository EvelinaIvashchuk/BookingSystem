using BookingSystem.Helpers;
using BookingSystem.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BookingSystem.Controllers;

/// <summary>
/// Public-facing resource browsing. No authentication required.
/// </summary>
public class ResourceController(IResourceService resourceService) : Controller
{
    private const int PageSize = 6;  // cards per page (2 rows of 3)

    // GET /Resource?search=room&categoryId=1&page=2
    public async Task<IActionResult> Index(string? search, int? categoryId, int page = 1)
    {
        var resources = await resourceService.GetAvailableResourcesAsync();

        // Filter by category
        if (categoryId.HasValue)
            resources = resources.Where(r => r.CategoryId == categoryId.Value);

        // Keyword search
        if (!string.IsNullOrWhiteSpace(search))
            resources = resources.Where(r =>
                r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (r.Description != null &&
                 r.Description.Contains(search, StringComparison.OrdinalIgnoreCase)));

        // Paginate
        var paged = PaginatedList<Models.Resource>.Create(resources, page, PageSize);

        // Populate filter state for the view
        ViewBag.Search     = search;
        ViewBag.CategoryId = categoryId;
        ViewBag.Categories = (await resourceService.GetAllCategoriesAsync())
            .Select(c => new SelectListItem
            {
                Value    = c.Id.ToString(),
                Text     = c.Name,
                Selected = c.Id == categoryId
            });

        return View(paged);
    }

    // GET /Resource/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var resource = await resourceService.GetResourceByIdAsync(id);

        if (resource is null)
            return NotFound();

        return View(resource);
    }
}
