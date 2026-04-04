using BookingSystem.Enums;
using BookingSystem.Models;

namespace BookingSystem.Data.Repositories;

public interface IResourceRepository : IGenericRepository<Resource>
{
    /// <summary>All resources with their Category included.</summary>
    Task<IEnumerable<Resource>> GetAllWithCategoryAsync();

    /// <summary>Only Available resources with Category included.</summary>
    Task<IEnumerable<Resource>> GetAvailableWithCategoryAsync();

    /// <summary>Single resource with Category included.</summary>
    Task<Resource?> GetWithCategoryAsync(int id);

    /// <summary>True when the resource exists and its status is Available.</summary>
    Task<bool> IsBookableAsync(int resourceId);
}
