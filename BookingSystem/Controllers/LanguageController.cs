using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace BookingSystem.Controllers;

public class LanguageController : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Set(string culture, string returnUrl = "/")
    {
        try
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions
                {
                    Expires    = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite   = SameSiteMode.Lax
                });
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            // If the culture is invalid, we just redirect back without setting the cookie.
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
