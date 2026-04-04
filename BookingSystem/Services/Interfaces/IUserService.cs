using BookingSystem.Models;

namespace BookingSystem.Services.Interfaces;

public interface IUserService
{
    /// <summary>All registered users with their roles, ordered by registration date.</summary>
    Task<IEnumerable<(ApplicationUser User, IList<string> Roles)>> GetAllUsersWithRolesAsync();

    Task<ApplicationUser?> GetByIdAsync(string userId);

    /// <summary>
    /// Toggles the IsActive flag. Inactive users cannot log in.
    /// Returns failure if the target is the only Admin (safety guard).
    /// </summary>
    Task<ServiceResult> SetActiveStatusAsync(string userId, bool isActive);

    /// <summary>Promotes a User-role account to Admin.</summary>
    Task<ServiceResult> PromoteToAdminAsync(string userId);
}
