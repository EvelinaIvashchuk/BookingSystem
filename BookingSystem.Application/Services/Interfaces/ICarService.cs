using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;

namespace BookingSystem.Services.Interfaces;

public interface ICarService
{
    Task<IEnumerable<Car>> GetAllCarsAsync();
    Task<IEnumerable<Car>> GetAvailableCarsAsync();
    Task<PagedResult<Car>> SearchCatalogAsync(CarCatalogQuery query, int page, int pageSize);
    Task<IReadOnlyList<string>> GetCitiesAsync();
    Task<Car?> GetCarByIdAsync(int id);
    Task<IEnumerable<Category>> GetAllCategoriesAsync();
    Task<ServiceResult<Car>> CreateCarAsync(Car car);
    Task<ServiceResult> UpdateCarAsync(Car car);
    Task<ServiceResult> SetCarStatusAsync(int carId, CarStatus status);
}
