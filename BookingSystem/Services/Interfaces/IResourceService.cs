using BookingSystem.Enums;
using BookingSystem.Models;

namespace BookingSystem.Services.Interfaces;

public interface IResourceService
{
    Task<IEnumerable<Resource>> GetAllResourcesAsync();
    Task<IEnumerable<Resource>> GetAvailableResourcesAsync();
    Task<Resource?>             GetResourceByIdAsync(int id);
    Task<IEnumerable<Category>> GetAllCategoriesAsync();

    /// <summary>Admin: adds a new resource. Returns the saved entity.</summary>
    Task<ServiceResult<Resource>> CreateResourceAsync(Resource resource);

    /// <summary>Admin: updates an existing resource.</summary>
    Task<ServiceResult> UpdateResourceAsync(Resource resource);

    /// <summary>Admin: changes the operational status of a resource.</summary>
    Task<ServiceResult> SetResourceStatusAsync(int resourceId, ResourceStatus status);
}
