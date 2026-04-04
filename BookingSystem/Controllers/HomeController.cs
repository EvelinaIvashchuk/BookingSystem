using System.Diagnostics;
using BookingSystem.Models;
using BookingSystem.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookingSystem.Controllers;

public class HomeController(IResourceService resourceService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var resources = await resourceService.GetAvailableResourcesAsync();
        return View(resources);
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    /// <summary>
    /// Handles non-exception HTTP status codes (404, 403, etc.)
    /// triggered by UseStatusCodePagesWithReExecute.
    /// </summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult StatusCode(int code)
    {
        ViewBag.StatusCode = code;
        return code switch
        {
            404 => View("NotFound"),
            403 => View("Forbidden"),
            _   => View("GenericError")
        };
    }
}
