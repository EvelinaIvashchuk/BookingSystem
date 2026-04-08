using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Services;

public class CarService(
    ICarRepository carRepo,
    ApplicationDbContext db,
    ILogger<CarService> logger) : ICarService
{
    public Task<IEnumerable<Car>> GetAllCarsAsync() =>
        carRepo.GetAllWithCategoryAsync();

    public Task<IEnumerable<Car>> GetAvailableCarsAsync() =>
        carRepo.GetAvailableWithCategoryAsync();

    public Task<Car?> GetCarByIdAsync(int id) =>
        carRepo.GetWithCategoryAsync(id);

    public async Task<IEnumerable<Category>> GetAllCategoriesAsync() =>
        await db.Categories.OrderBy(c => c.Name).ToListAsync();

    public async Task<ServiceResult<Car>> CreateCarAsync(Car car)
    {
        var plateExists = await carRepo.AnyAsync(
            c => c.LicensePlate == car.LicensePlate);

        if (plateExists)
            return ServiceResult<Car>.Fail(
                "A car with this license plate already exists.");

        car.Status = CarStatus.Available;

        await carRepo.AddAsync(car);
        await carRepo.SaveChangesAsync();

        logger.LogInformation("Car {CarId} \"{Brand} {Model}\" created", car.Id, car.Brand, car.Model);
        return ServiceResult<Car>.Ok(car);
    }

    public async Task<ServiceResult> UpdateCarAsync(Car car)
    {
        var exists = await carRepo.GetByIdAsync(car.Id);
        if (exists is null)
            return ServiceResult.Fail("Car not found.");

        var plateConflict = await carRepo.AnyAsync(
            c => c.LicensePlate == car.LicensePlate && c.Id != car.Id);

        if (plateConflict)
            return ServiceResult.Fail(
                "Another car with this license plate already exists.");

        carRepo.Update(car);
        await carRepo.SaveChangesAsync();

        logger.LogInformation("Car {CarId} updated", car.Id);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SetCarStatusAsync(int carId, CarStatus status)
    {
        var car = await carRepo.GetByIdAsync(carId);
        if (car is null)
            return ServiceResult.Fail("Car not found.");

        car.Status = status;
        carRepo.Update(car);
        await carRepo.SaveChangesAsync();

        logger.LogInformation(
            "Car {CarId} status changed to {Status}", carId, status);
        return ServiceResult.Ok();
    }
}
