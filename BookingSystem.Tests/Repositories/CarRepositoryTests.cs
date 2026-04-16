using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace BookingSystem.Tests.Repositories;

/// <summary>
/// Unit-тести для <see cref="CarRepository"/>.
///
/// Підхід:
///  • InMemory БД — тестує реальні EF-запити (Include, OrderBy, Where).
///  • Moq-стаби    — демонструє ізоляцію залежностей через <see cref="ICarRepository"/>.
/// </summary>
public class CarRepositoryTests
{
    // =========================================================================
    //  Допоміжні методи
    // =========================================================================

    private static Category MakeCategory(int id = 1, string name = "Sedan") =>
        new() { Id = id, Name = name };

    private static Car MakeCar(
        int         id,
        string      brand,
        string      model,
        Category    category,
        CarStatus   status      = CarStatus.Available,
        decimal     pricePerDay = 1000m) =>
        new()
        {
            Id           = id,
            Brand        = brand,
            Model        = model,
            Year         = 2023,
            LicensePlate = $"AA{id:D4}BB",
            CategoryId   = category.Id,
            Category     = category,
            Status       = status,
            PricePerDay  = pricePerDay,
            Location     = "Kyiv",
            Seats        = 5,
            FuelType     = FuelType.Petrol,
            Transmission = Transmission.Automatic
        };

    // =========================================================================
    //  GetAllWithCategoryAsync — InMemory
    // =========================================================================

    [Fact]
    public async Task GetAllWithCategoryAsync_WhenThreeCarsExist_ReturnsAllThreeCars()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var sedan = MakeCategory(1, "Sedan");
        var suv   = MakeCategory(2, "SUV");
        db.Categories.AddRange(sedan, suv);
        db.Cars.AddRange(
            MakeCar(1, "Toyota",  "Camry",  sedan),
            MakeCar(2, "Honda",   "CR-V",   suv),
            MakeCar(3, "Hyundai", "Tucson", suv));
        await db.SaveChangesAsync();

        var sut = new CarRepository(db);

        // Act
        var result = await sut.GetAllWithCategoryAsync();

        // Assert
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAllWithCategoryAsync_WhenCarsExist_EachCarHasCategoryLoaded()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var sedan = MakeCategory(1, "Sedan");
        db.Categories.Add(sedan);
        db.Cars.Add(MakeCar(1, "Toyota", "Camry", sedan));
        await db.SaveChangesAsync();

        var sut = new CarRepository(db);

        // Act
        var result = (await sut.GetAllWithCategoryAsync()).ToList();

        // Assert
        result.Should().AllSatisfy(c => c.Category.Should().NotBeNull());
    }

    [Fact]
    public async Task GetAllWithCategoryAsync_WhenNoCarsInDatabase_ReturnsEmptyCollection()
    {
        // Arrange
        await using var db  = TestDbContextFactory.Create();
        var sut = new CarRepository(db);

        // Act
        var result = await sut.GetAllWithCategoryAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllWithCategoryAsync_WhenCarsHaveDifferentCategories_ReturnsSortedByCategoryThenBrand()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var suv   = MakeCategory(1, "SUV");
        var sedan = MakeCategory(2, "Sedan");
        db.Categories.AddRange(suv, sedan);
        db.Cars.AddRange(
            MakeCar(1, "Toyota", "Camry",  sedan),  // Sedan / Toyota
            MakeCar(2, "BMW",    "X5",     suv),    // SUV   / BMW
            MakeCar(3, "Audi",   "A4",     sedan),  // Sedan / Audi
            MakeCar(4, "Honda",  "CR-V",   suv));   // SUV   / Honda
        await db.SaveChangesAsync();

        var sut = new CarRepository(db);

        // Act
        var result = (await sut.GetAllWithCategoryAsync()).ToList();

        // Assert — очікуємо: Sedan/Audi, Sedan/Toyota, SUV/BMW, SUV/Honda
        // ("Sedan" < "SUV" alphabetically)
        result.Select(c => c.Brand)
              .Should().ContainInOrder("Audi", "Toyota", "BMW", "Honda");
    }

    // =========================================================================
    //  GetAvailableWithCategoryAsync — InMemory
    // =========================================================================

    [Fact]
    public async Task GetAvailableWithCategoryAsync_WhenMixedStatusCars_ReturnsOnlyAvailableCars()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var sedan = MakeCategory(1, "Sedan");
        db.Categories.Add(sedan);
        db.Cars.AddRange(
            MakeCar(1, "Toyota",  "Camry", sedan, CarStatus.Available),
            MakeCar(2, "Honda",   "Civic", sedan, CarStatus.Rented),
            MakeCar(3, "Hyundai", "Elantra",sedan,CarStatus.UnderMaintenance),
            MakeCar(4, "Kia",     "K5",    sedan, CarStatus.Available));
        await db.SaveChangesAsync();

        var sut = new CarRepository(db);

        // Act
        var result = (await sut.GetAvailableWithCategoryAsync()).ToList();

        // Assert
        result.Should().HaveCount(2)
              .And.OnlyContain(c => c.Status == CarStatus.Available);
    }

    [Fact]
    public async Task GetAvailableWithCategoryAsync_WhenAllCarsRented_ReturnsEmptyCollection()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var sedan = MakeCategory(1, "Sedan");
        db.Categories.Add(sedan);
        db.Cars.Add(MakeCar(1, "Toyota", "Camry", sedan, CarStatus.Rented));
        await db.SaveChangesAsync();

        var sut = new CarRepository(db);

        // Act
        var result = await sut.GetAvailableWithCategoryAsync();

        // Assert
        result.Should().BeEmpty();
    }

    // =========================================================================
    //  GetWithCategoryAsync — InMemory
    // =========================================================================

    [Fact]
    public async Task GetWithCategoryAsync_WhenCarWithGivenIdExists_ReturnsCarWithCategoryLoaded()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var sedan = MakeCategory(1, "Sedan");
        db.Categories.Add(sedan);
        var car = MakeCar(7, "Toyota", "Camry", sedan);
        db.Cars.Add(car);
        await db.SaveChangesAsync();

        var sut = new CarRepository(db);

        // Act
        var result = await sut.GetWithCategoryAsync(7);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(7);
        result.Category.Should().NotBeNull();
        result.Category.Name.Should().Be("Sedan");
    }

    [Fact]
    public async Task GetWithCategoryAsync_WhenNoCarWithGivenId_ReturnsNull()
    {
        // Arrange
        await using var db  = TestDbContextFactory.Create();
        var sut = new CarRepository(db);

        // Act
        var result = await sut.GetWithCategoryAsync(999);

        // Assert
        result.Should().BeNull();
    }

    // =========================================================================
    //  IsRentableAsync — InMemory
    // =========================================================================

    [Fact]
    public async Task IsRentableAsync_WhenCarExistsAndIsAvailable_ReturnsTrue()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var sedan = MakeCategory(1, "Sedan");
        db.Categories.Add(sedan);
        db.Cars.Add(MakeCar(1, "Toyota", "Camry", sedan, CarStatus.Available));
        await db.SaveChangesAsync();

        var sut = new CarRepository(db);

        // Act
        var result = await sut.IsRentableAsync(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsRentableAsync_WhenCarExistsButIsRented_ReturnsFalse()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var sedan = MakeCategory(1, "Sedan");
        db.Categories.Add(sedan);
        db.Cars.Add(MakeCar(1, "Toyota", "Camry", sedan, CarStatus.Rented));
        await db.SaveChangesAsync();

        var sut = new CarRepository(db);

        // Act
        var result = await sut.IsRentableAsync(1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsRentableAsync_WhenCarDoesNotExist_ReturnsFalse()
    {
        // Arrange
        await using var db  = TestDbContextFactory.Create();
        var sut = new CarRepository(db);

        // Act
        var result = await sut.IsRentableAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsRentableAsync_WhenCarIsUnderMaintenance_ReturnsFalse()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var sedan = MakeCategory(1, "Sedan");
        db.Categories.Add(sedan);
        db.Cars.Add(MakeCar(1, "Toyota", "Camry", sedan, CarStatus.UnderMaintenance));
        await db.SaveChangesAsync();

        var sut = new CarRepository(db);

        // Act
        var result = await sut.IsRentableAsync(1);

        // Assert
        result.Should().BeFalse();
    }

    // =========================================================================
    //  Moq — демонстрація стабів і моків ICarRepository
    // =========================================================================

    [Fact]
    public async Task GetAllWithCategoryAsync_WhenRepositoryStubbed_ReturnsStubbedCollection()
    {
        // Arrange — stub: метод повертає фіксований список без реального EF запиту
        var sedan  = MakeCategory(1, "Sedan");
        var stubData = new List<Car>
        {
            MakeCar(1, "Toyota", "Camry", sedan),
            MakeCar(2, "Honda",  "Civic", sedan)
        };

        var mockRepo = new Mock<ICarRepository>();
        mockRepo
            .Setup(r => r.GetAllWithCategoryAsync())
            .ReturnsAsync(stubData);

        // Act
        var result = await mockRepo.Object.GetAllWithCategoryAsync();

        // Assert
        result.Should().HaveCount(2)
              .And.Contain(c => c.Brand == "Toyota")
              .And.Contain(c => c.Brand == "Honda");
    }

    [Fact]
    public async Task IsRentableAsync_WhenMockedAsAvailable_CallsRepositoryOnce()
    {
        // Arrange — mock: перевіряємо, що метод викликається рівно один раз
        var mockRepo = new Mock<ICarRepository>();
        mockRepo
            .Setup(r => r.IsRentableAsync(It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        var result = await mockRepo.Object.IsRentableAsync(42);

        // Assert — значення
        result.Should().BeTrue();

        // Assert — взаємодія: метод викликався рівно один раз із будь-яким int
        mockRepo.Verify(r => r.IsRentableAsync(It.IsAny<int>()), Times.Once());
    }

    [Fact]
    public async Task GetWithCategoryAsync_WhenRepositoryReturnsNull_ResultIsNull()
    {
        // Arrange — stub повертає null (автомобіль не знайдено)
        var mockRepo = new Mock<ICarRepository>();
        mockRepo
            .Setup(r => r.GetWithCategoryAsync(It.IsAny<int>()))
            .ReturnsAsync((Car?)null);

        // Act
        var result = await mockRepo.Object.GetWithCategoryAsync(999);

        // Assert
        result.Should().BeNull();
        mockRepo.Verify(r => r.GetWithCategoryAsync(999), Times.Once());
    }

    [Fact]
    public async Task GetAvailableWithCategoryAsync_WhenRepositoryReturnsEmptyList_ResultIsEmpty()
    {
        // Arrange — stub для порожнього парку автомобілів
        var mockRepo = new Mock<ICarRepository>();
        mockRepo
            .Setup(r => r.GetAvailableWithCategoryAsync())
            .ReturnsAsync(new List<Car>());

        // Act
        var result = await mockRepo.Object.GetAvailableWithCategoryAsync();

        // Assert
        result.Should().BeEmpty();
    }
}
