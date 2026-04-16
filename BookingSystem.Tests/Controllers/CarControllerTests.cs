using BookingSystem.Controllers;
using BookingSystem.Enums;
using BookingSystem.Helpers;
using BookingSystem.Models;
using BookingSystem.Services.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BookingSystem.Tests.Controllers;

/// <summary>
/// Unit-тести для <see cref="CarController"/>.
///
/// Підхід: контролер інстанціюється напряму без HTTP-пайплайну.
/// Всі залежності замінені Moq-стабами.
/// Перевіряється маршрутизація дій (тип результату, модель, ViewBag).
/// </summary>
public class CarControllerTests
{
    // =========================================================================
    //  Допоміжні методи
    // =========================================================================

    private readonly Mock<ICarService> _carService = new();

    private CarController CreateSut() => new(_carService.Object);

    private static Category MakeCategory(int id = 1, string name = "Sedan") =>
        new() { Id = id, Name = name };

    private static Car MakeCar(
        int      id,
        string   brand,
        string   model,
        int      categoryId = 1,
        CarStatus status    = CarStatus.Available,
        string?  description = null) =>
        new()
        {
            Id           = id,
            Brand        = brand,
            Model        = model,
            Year         = 2023,
            LicensePlate = $"AA{id:D4}BB",
            CategoryId   = categoryId,
            Category     = MakeCategory(categoryId),
            Status       = status,
            PricePerDay  = 1000m,
            Location     = "Kyiv",
            Seats        = 5,
            FuelType     = FuelType.Petrol,
            Transmission = Transmission.Automatic,
            Description  = description
        };

    // =========================================================================
    //  Index — пошук і фільтрація
    // =========================================================================

    [Fact]
    public async Task Index_WhenNoFilters_ReturnsViewWithAllAvailableCars()
    {
        var cars = new[]
        {
            MakeCar(1, "Toyota", "Camry"),
            MakeCar(2, "Honda",  "Civic"),
            MakeCar(3, "BMW",    "X5", categoryId: 2)
        };
        _carService.Setup(s => s.GetAvailableCarsAsync()).ReturnsAsync(cars);
        _carService.Setup(s => s.GetAllCategoriesAsync()).ReturnsAsync(Array.Empty<Category>());

        var result = await CreateSut().Index(search: null, categoryId: null, page: 1);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        var model = view.Model.Should().BeOfType<PaginatedList<Car>>().Subject;
        model.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Index_WhenSearchByBrand_ReturnsOnlyMatchingCars()
    {
        var cars = new[]
        {
            MakeCar(1, "Toyota", "Camry"),
            MakeCar(2, "Honda",  "Civic"),
            MakeCar(3, "Toyota", "Corolla")
        };
        _carService.Setup(s => s.GetAvailableCarsAsync()).ReturnsAsync(cars);
        _carService.Setup(s => s.GetAllCategoriesAsync()).ReturnsAsync(Array.Empty<Category>());

        var result = await CreateSut().Index(search: "toyota", categoryId: null, page: 1);

        var model = ((ViewResult)result).Model.Should().BeOfType<PaginatedList<Car>>().Subject;
        model.TotalCount.Should().Be(2);
        model.Should().OnlyContain(c => c.Brand == "Toyota");
    }

    [Fact]
    public async Task Index_WhenSearchByModel_ReturnsOnlyMatchingCars()
    {
        var cars = new[]
        {
            MakeCar(1, "Toyota", "Camry"),
            MakeCar(2, "Honda",  "CR-V"),
            MakeCar(3, "BMW",    "X5")
        };
        _carService.Setup(s => s.GetAvailableCarsAsync()).ReturnsAsync(cars);
        _carService.Setup(s => s.GetAllCategoriesAsync()).ReturnsAsync(Array.Empty<Category>());

        var result = await CreateSut().Index(search: "cr-v", categoryId: null, page: 1);

        var model = ((ViewResult)result).Model.Should().BeOfType<PaginatedList<Car>>().Subject;
        model.TotalCount.Should().Be(1);
        model.Single().Model.Should().Be("CR-V");
    }

    [Fact]
    public async Task Index_WhenSearchByDescription_ReturnsMatchingCars()
    {
        var cars = new[]
        {
            MakeCar(1, "Toyota", "Camry",  description: "Comfortable family sedan"),
            MakeCar(2, "Honda",  "Civic",  description: "Sporty city car"),
            MakeCar(3, "BMW",    "X5",     description: "Luxury SUV")
        };
        _carService.Setup(s => s.GetAvailableCarsAsync()).ReturnsAsync(cars);
        _carService.Setup(s => s.GetAllCategoriesAsync()).ReturnsAsync(Array.Empty<Category>());

        var result = await CreateSut().Index(search: "luxury", categoryId: null, page: 1);

        var model = ((ViewResult)result).Model.Should().BeOfType<PaginatedList<Car>>().Subject;
        model.TotalCount.Should().Be(1);
        model.Single().Brand.Should().Be("BMW");
    }

    [Fact]
    public async Task Index_WhenFilteredByCategoryId_ReturnsOnlyCarsInThatCategory()
    {
        var cars = new[]
        {
            MakeCar(1, "Toyota", "Camry",   categoryId: 1), // Sedan
            MakeCar(2, "Honda",  "CR-V",    categoryId: 2), // SUV
            MakeCar(3, "BMW",    "X5",      categoryId: 2), // SUV
            MakeCar(4, "Kia",    "Sorento", categoryId: 2)  // SUV
        };
        _carService.Setup(s => s.GetAvailableCarsAsync()).ReturnsAsync(cars);
        _carService.Setup(s => s.GetAllCategoriesAsync()).ReturnsAsync(Array.Empty<Category>());

        var result = await CreateSut().Index(search: null, categoryId: 2, page: 1);

        var model = ((ViewResult)result).Model.Should().BeOfType<PaginatedList<Car>>().Subject;
        model.TotalCount.Should().Be(3);
        model.Should().OnlyContain(c => c.CategoryId == 2);
    }

    [Fact]
    public async Task Index_WhenBothSearchAndCategoryApplied_ReturnsIntersection()
    {
        var cars = new[]
        {
            MakeCar(1, "Toyota", "Camry",  categoryId: 1),  // Sedan / Toyota
            MakeCar(2, "Toyota", "RAV4",   categoryId: 2),  // SUV   / Toyota — match
            MakeCar(3, "Honda",  "CR-V",   categoryId: 2),  // SUV   / Honda
            MakeCar(4, "Toyota", "Corolla",categoryId: 1)   // Sedan / Toyota
        };
        _carService.Setup(s => s.GetAvailableCarsAsync()).ReturnsAsync(cars);
        _carService.Setup(s => s.GetAllCategoriesAsync()).ReturnsAsync(Array.Empty<Category>());

        var result = await CreateSut().Index(search: "toyota", categoryId: 2, page: 1);

        var model = ((ViewResult)result).Model.Should().BeOfType<PaginatedList<Car>>().Subject;
        model.TotalCount.Should().Be(1);
        model.Single().Model.Should().Be("RAV4");
    }

    [Fact]
    public async Task Index_PopulatesViewBagWithSearchAndCategoryId()
    {
        _carService.Setup(s => s.GetAvailableCarsAsync()).ReturnsAsync(Array.Empty<Car>());
        _carService.Setup(s => s.GetAllCategoriesAsync()).ReturnsAsync(Array.Empty<Category>());

        var sut = CreateSut();
        await sut.Index(search: "bmw", categoryId: 3, page: 1);

        ((string?)sut.ViewBag.Search).Should().Be("bmw");
        ((int?)sut.ViewBag.CategoryId).Should().Be(3);
    }

    // =========================================================================
    //  Details
    // =========================================================================

    [Fact]
    public async Task Details_WhenCarExists_ReturnsViewWithCar()
    {
        var car = MakeCar(7, "Toyota", "Camry");
        _carService.Setup(s => s.GetCarByIdAsync(7)).ReturnsAsync(car);

        var result = await CreateSut().Details(7);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.Model.Should().BeSameAs(car);
    }

    [Fact]
    public async Task Details_WhenCarDoesNotExist_ReturnsNotFound()
    {
        _carService.Setup(s => s.GetCarByIdAsync(It.IsAny<int>()))
                   .ReturnsAsync((Car?)null);

        var result = await CreateSut().Details(999);

        result.Should().BeOfType<NotFoundResult>();
    }
}
