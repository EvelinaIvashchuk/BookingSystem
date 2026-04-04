using BookingSystem.Models;
using BookingSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BookingSystem.Controllers;

public class AccountController(
    UserManager<ApplicationUser>  userManager,
    SignInManager<ApplicationUser> signInManager) : Controller
{
    // ── Register ──────────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var user = new ApplicationUser
        {
            UserName       = vm.Email,
            Email          = vm.Email,
            FirstName      = vm.FirstName.Trim(),
            LastName       = vm.LastName.Trim(),
            EmailConfirmed = true   // Skip email confirmation for coursework
        };

        var result = await userManager.CreateAsync(user, vm.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(vm);
        }

        await userManager.AddToRoleAsync(user, "User");
        await signInManager.SignInAsync(user, isPersistent: false);

        return RedirectToAction("Index", "Home");
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid)
            return View(vm);

        // Check IsActive before attempting sign-in (avoids leaking account existence)
        var user = await userManager.FindByEmailAsync(vm.Email);
        if (user is not null && !user.IsActive)
        {
            ModelState.AddModelError(string.Empty,
                "Your account has been deactivated. Please contact an administrator.");
            return View(vm);
        }

        var result = await signInManager.PasswordSignInAsync(
            vm.Email,
            vm.Password,
            vm.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
                return Redirect(vm.ReturnUrl);

            return RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty,
                "Account locked due to too many failed attempts. Try again in 10 minutes.");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
        }

        return View(vm);
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    // ── Access Denied ─────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
