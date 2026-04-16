using BookingSystem.Data;
using BookingSystem.Models;
using BookingSystem.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Services;

public class UserService(UserManager<ApplicationUser>  userManager, ApplicationDbContext db, 
    ILogger<UserService> logger) : IUserService
{
    public async Task<IEnumerable<(ApplicationUser User, IList<string> Roles)>> GetAllUsersWithRolesAsync()
    {
        var users = await db.Users
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();

        var result = new List<(ApplicationUser, IList<string>)>();

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            result.Add((user, roles));
        }

        return result;
    }

    public async Task<ApplicationUser?> GetByIdAsync(string userId) =>
        await userManager.FindByIdAsync(userId);

    public async Task<ServiceResult> SetActiveStatusAsync(string userId, bool isActive)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return ServiceResult.Fail("User not found.");

        // Safety guard: do not deactivate the last Admin
        if (!isActive && await userManager.IsInRoleAsync(user, "Admin"))
        {
            var adminCount = (await userManager.GetUsersInRoleAsync("Admin")).Count;
            if (adminCount <= 1)
                return ServiceResult.Fail(
                    "Cannot deactivate the only Admin account. Promote another user first.");
        }

        user.IsActive = isActive;
        var result = await userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ServiceResult.Fail($"Update failed: {errors}");
        }

        logger.LogInformation(
            "User {UserId} ({Email}) set IsActive={IsActive}",
            userId, user.Email, isActive);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> PromoteToAdminAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return ServiceResult.Fail("User not found.");

        if (await userManager.IsInRoleAsync(user, "Admin"))
            return ServiceResult.Fail("User is already an Admin.");

        await userManager.RemoveFromRoleAsync(user, "User");
        var result = await userManager.AddToRoleAsync(user, "Admin");

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ServiceResult.Fail($"Promotion failed: {errors}");
        }

        logger.LogInformation("User {UserId} ({Email}) promoted to Admin", userId, user.Email);
        return ServiceResult.Ok();
    }
}
