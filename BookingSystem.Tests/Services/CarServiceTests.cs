using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq.Expressions;

namespace BookingSystem.Tests.Services;

/// <summary>
/// Unit-тести для <see cref="CarService"/>.
///
/// Підхід:
///  • <see cref="ICarRepository"/> — Moq-стаб (ізоляція від EF).
///  • <see cref="ApplicationDbContext"/> — InMemory БД лише для методів,
///    що звертаються до DbContext напряму (GetAllCategoriesAsync).
/// </summary>
public class CarServiceTests
{
    private readonly Mock<ICarRepository>    _carRepo = new();
    private readonly Mock<ILogger<CarService>> _logger = new();

    private CarService CreateSut(ApplicationDbContext? db = null) =>
        new(_carRepo.Object, db ?? TestDbContextFactory.Create(), _logger.Object);

    private static Car MakeCar(
        int     id    = 1,
        string  plate = "AA0001BB",
        decimal price = 1000m) =>
        new()
        {
            Id           = id,
            Brand        = "Toyota",
            Model        = "Camry",
            Year         = 2023,
            LicensePlate = plate,
            CategoryId   = 1,
            PricePerDay  = price,
            Location     = "Kyiv",
            Seats        = 5,
            FuelType     = FuelType.Petrol,
            Transmission = Transmission.Automatic
        };

    // =========================================================================
    //  CreateCarAsync
    // =========================================================================

    [Fact]
    public async Task CreateCarAsync_WhenLicensePlateAlreadyExists_ReturnsFailure()
    {
        _carRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Car, bool>>>()))
                .ReturnsAsync(true);

        var result = await CreateSut().CreateCarAsync(MakeCar());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("license plate");
    }

    [Fact]
    public async Task CreateCarAsync_WhenLicensePlateIsUnique_ForcesStatusToAvailable()
    {
        _carRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Car, bool>>>()))
                .ReturnsAsync(false);
        _carRepo.Setup(r => r.AddAsync(It.IsAny<Car>())).Returns(Task.CompletedTask);
        _carRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var car = MakeCar();
        car.Status = CarStatus.UnderMaintenance; // навмисно задаємо інший статус

        await CreateSut().CreateCarAsync(car);

        // Сервіс повинен перевизначити статус на Available незалежно від переданого
        car.Status.Should().Be(CarStatus.Available);
    }

    [Fact]
    public async Task CreateCarAsync_WhenLicensePlateIsUnique_ReturnsSuccessWithCarValue()
    {
        _carRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Car, bool>>>()))
                .ReturnsAsync(false);
        _carRepo.Setup(r => r.AddAsync(It.IsAny<Car>())).Returns(Task.CompletedTask);
        _carRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var car    = MakeCar();
        var result = await CreateSut().CreateCarAsync(car);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(car);
    }

    // =========================================================================
    //  UpdateCarAsync
    // =========================================================================

    [Fact]
    public async Task UpdateCarAsync_WhenCarNotFound_ReturnsFailure()
    {
        _carRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((Car?)null);

        var result = await CreateSut().UpdateCarAsync(MakeCar(id: 99));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task UpdateCarAsync_WhenAnotherCarHasSameLicensePlate_ReturnsFailure()
    {
        _carRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(MakeCar(1, "AA0001BB"));
        _carRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Car, bool>>>()))
                .ReturnsAsync(true); // конфлікт номерного знаку з іншим авто

        var result = await CreateSut().UpdateCarAsync(MakeCar(1, "AA0001BB"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("license plate");
    }

    [Fact]
    public async Task UpdateCarAsync_WhenNoConflict_UpdatesAllFieldsAndReturnsSuccess()
    {
        var existing = MakeCar(1, "OLD001BB");
        _carRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);
        _carRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Car, bool>>>()))
                .ReturnsAsync(false);
        _carRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var incoming = new Car
        {
            Id           = 1,
            Brand        = "Honda",
            Model        = "Civic",
            Year         = 2024,
            LicensePlate = "NEW002CC",
            FuelType     = FuelType.Electric,
            Transmission = Transmission.Automatic,
            Seats        = 4,
            PricePerDay  = 1500m,
            Description  = "Updated desc",
            Location     = "Lviv",
            CategoryId   = 2,
            ImageUrl     = "cars/new-image.jpg",
            Status       = CarStatus.Rented
        };

        var result = await CreateSut().UpdateCarAsync(incoming);

        result.IsSuccess.Should().BeTrue();

        // Перевіряємо, що всі поля справді скопійовані в existing
        existing.Brand.Should().Be("Honda");
        existing.Model.Should().Be("Civic");
        existing.Year.Should().Be(2024);
        existing.LicensePlate.Should().Be("NEW002CC");
        existing.FuelType.Should().Be(FuelType.Electric);
        existing.Transmission.Should().Be(Transmission.Automatic);
        existing.Seats.Should().Be(4);
        existing.PricePerDay.Should().Be(1500m);
        existing.Description.Should().Be("Updated desc");
        existing.Location.Should().Be("Lviv");
        existing.CategoryId.Should().Be(2);
        existing.ImageUrl.Should().Be("cars/new-image.jpg");
        existing.Status.Should().Be(CarStatus.Rented);
    }

    // =========================================================================
    //  SetCarStatusAsync
    // =========================================================================

    [Fact]
    public async Task SetCarStatusAsync_WhenCarNotFound_ReturnsFailure()
    {
        _carRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
                .ReturnsAsync((Car?)null);

        var result = await CreateSut().SetCarStatusAsync(99, CarStatus.Rented);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Theory]
    [InlineData(CarStatus.Rented)]
    [InlineData(CarStatus.UnderMaintenance)]
    [InlineData(CarStatus.Available)]
    public async Task SetCarStatusAsync_WhenCarExists_ChangesStatusAndReturnsSuccess(CarStatus newStatus)
    {
        var car = MakeCar(1);
        car.Status = CarStatus.Available;

        _carRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(car);
        _carRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var result = await CreateSut().SetCarStatusAsync(1, newStatus);

        result.IsSuccess.Should().BeTrue();
        car.Status.Should().Be(newStatus);

        _carRepo.Verify(r => r.Update(car), Times.Once());
        _carRepo.Verify(r => r.SaveChangesAsync(), Times.Once());
    }

    // =========================================================================
    //  GetAllCategoriesAsync — InMemory DbContext
    // =========================================================================

    [Fact]
    public async Task GetAllCategoriesAsync_WhenCategoriesExist_ReturnsSortedByNameAscending()
    {
        await using var db = TestDbContextFactory.Create();
        db.Categories.AddRange(
            new Category { Id = 1, Name = "SUV" },
            new Category { Id = 2, Name = "Minivan" },
            new Category { Id = 3, Name = "Sedan" });
        await db.SaveChangesAsync();

        var result = (await CreateSut(db).GetAllCategoriesAsync()).ToList();

        result.Should().HaveCount(3);
        // InMemory EF порівнює рядки без урахування регістру: Minivan < Sedan < SUV
        result.Select(c => c.Name)
              .Should().ContainInOrder("Minivan", "Sedan", "SUV");
    }
}
