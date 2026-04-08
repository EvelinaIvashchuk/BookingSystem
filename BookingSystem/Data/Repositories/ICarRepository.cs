using BookingSystem.Models;

namespace BookingSystem.Data.Repositories;

public interface ICarRepository : IGenericRepository<Car>
{
    Task<IEnumerable<Car>> GetAllWithCategoryAsync();
    Task<IEnumerable<Car>> GetAvailableWithCategoryAsync();
    Task<Car?> GetWithCategoryAsync(int id);
    Task<bool> IsRentableAsync(int carId);
}
