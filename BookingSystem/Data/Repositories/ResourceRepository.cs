using BookingSystem.Enums;
using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data.Repositories;

public class ResourceRepository(ApplicationDbContext db)
    : GenericRepository<Resource>(db), IResourceRepository
{
    public async Task<IEnumerable<Resource>> GetAllWithCategoryAsync() =>
        await Db.Resources
            .Include(r => r.Category)
            .OrderBy(r => r.Category.Name)
            .ThenBy(r => r.Name)
            .ToListAsync();

    public async Task<IEnumerable<Resource>> GetAvailableWithCategoryAsync() =>
        await Db.Resources
            .Where(r => r.Status == ResourceStatus.Available)
            .Include(r => r.Category)
            .OrderBy(r => r.Category.Name)
            .ThenBy(r => r.Name)
            .ToListAsync();

    public async Task<Resource?> GetWithCategoryAsync(int id) =>
        await Db.Resources
            .Include(r => r.Category)
            .FirstOrDefaultAsync(r => r.Id == id);

    public async Task<bool> IsBookableAsync(int resourceId) =>
        await Db.Resources
            .AnyAsync(r => r.Id == resourceId && r.Status == ResourceStatus.Available);
}
