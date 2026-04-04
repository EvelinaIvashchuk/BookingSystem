using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data.Repositories;

public class GenericRepository<T>(ApplicationDbContext db) : IGenericRepository<T>
    where T : class
{
    protected readonly ApplicationDbContext Db  = db;
    protected readonly DbSet<T>            Set  = db.Set<T>();

    public async Task<T?> GetByIdAsync(int id) =>
        await Set.FindAsync(id);

    public async Task<IEnumerable<T>> GetAllAsync() =>
        await Set.ToListAsync();

    public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate) =>
        await Set.Where(predicate).ToListAsync();

    public async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate) =>
        await Set.AnyAsync(predicate);

    public async Task AddAsync(T entity) =>
        await Set.AddAsync(entity);

    public void Update(T entity) =>
        Set.Update(entity);

    public void Delete(T entity) =>
        Set.Remove(entity);

    public async Task SaveChangesAsync() =>
        await Db.SaveChangesAsync();
}
