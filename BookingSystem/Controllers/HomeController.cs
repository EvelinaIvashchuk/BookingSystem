using System.Diagnostics;
using BookingSystem.Data;
using BookingSystem.Models;
using BookingSystem.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookingSystem.Controllers;

public class HomeController(ICarService carService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var cars = await carService.GetAvailableCarsAsync();
        return View(cars);
    }

    public IActionResult Privacy() => View();

    /// <summary>Authors and licences of the demo car photos (required by CC BY / CC BY-SA).</summary>
    public IActionResult Photos() =>
        View(DemoFleetPhotos.ByPlate.Values.OrderBy(p => p.Car, StringComparer.Ordinal).ToList());

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public new IActionResult StatusCode(int code)
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
