using BookingSystem.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookingSystem.Controllers;

[Route("image")]
public class ImageController(IStorageService storage, ILogger<ImageController> logger) : Controller
{
    [HttpGet("{**key}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)] // cache 24h in browser
    public async Task<IActionResult> Get(string key)
    {
        try
        {
            var (stream, contentType) = await storage.GetObjectAsync(key);
            return File(stream, contentType);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Image not found in storage: {Key}", key);
            return NotFound();
        }
    }
}
