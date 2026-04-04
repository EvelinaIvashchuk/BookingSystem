using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Services;

public class ResourceService(
    IResourceRepository resourceRepo,
    ApplicationDbContext db,
    ILogger<ResourceService> logger) : IResourceService
{
    public Task<IEnumerable<Resource>> GetAllResourcesAsync() =>
        resourceRepo.GetAllWithCategoryAsync();

    public Task<IEnumerable<Resource>> GetAvailableResourcesAsync() =>
        resourceRepo.GetAvailableWithCategoryAsync();

    public Task<Resource?> GetResourceByIdAsync(int id) =>
        resourceRepo.GetWithCategoryAsync(id);

    public async Task<IEnumerable<Category>> GetAllCategoriesAsync() =>
        await db.Categories.OrderBy(c => c.Name).ToListAsync();

    public async Task<ServiceResult<Resource>> CreateResourceAsync(Resource resource)
    {
        var nameExists = await resourceRepo.AnyAsync(
            r => r.Name == resource.Name && r.CategoryId == resource.CategoryId);

        if (nameExists)
            return ServiceResult<Resource>.Fail(
                "A resource with this name already exists in the selected category.");

        resource.Status = ResourceStatus.Available;

        await resourceRepo.AddAsync(resource);
        await resourceRepo.SaveChangesAsync();

        logger.LogInformation("Resource {ResourceId} \"{Name}\" created", resource.Id, resource.Name);
        return ServiceResult<Resource>.Ok(resource);
    }

    public async Task<ServiceResult> UpdateResourceAsync(Resource resource)
    {
        var exists = await resourceRepo.GetByIdAsync(resource.Id);
        if (exists is null)
            return ServiceResult.Fail("Resource not found.");

        // Prevent renaming to a name already taken within the same category
        var nameConflict = await resourceRepo.AnyAsync(
            r => r.Name == resource.Name &&
                 r.CategoryId == resource.CategoryId &&
                 r.Id != resource.Id);

        if (nameConflict)
            return ServiceResult.Fail(
                "Another resource with this name already exists in the selected category.");

        resourceRepo.Update(resource);
        await resourceRepo.SaveChangesAsync();

        logger.LogInformation("Resource {ResourceId} updated", resource.Id);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SetResourceStatusAsync(int resourceId, ResourceStatus status)
    {
        var resource = await resourceRepo.GetByIdAsync(resourceId);
        if (resource is null)
            return ServiceResult.Fail("Resource not found.");

        resource.Status = status;
        resourceRepo.Update(resource);
        await resourceRepo.SaveChangesAsync();

        logger.LogInformation(
            "Resource {ResourceId} status changed to {Status}", resourceId, status);
        return ServiceResult.Ok();
    }
}
