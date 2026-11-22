using BookingSystem.Controllers;
using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services.Interfaces;
using BookingSystem.ViewModels;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BookingSystem.Tests.Controllers;

/// <summary>
/// Unit-тести для <see cref="CarController"/>.
///
/// Підхід: контролер інстанціюється напряму без HTTP-пайплайну.
/// Всі залежності замінені Moq-стабами.
/// Перевіряється, які фільтри контролер передає в сервіс, як обробляє дати і що кладе в модель подання.
/// Саму фільтрацію перевіряють <see cref="Repositories.CarCatalogSearchTests"/>.
/// </summary>
public class CarControllerTests
{
    // =========================================================================
    //  Допоміжні методи
    // =========================================================================

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private readonly Mock<ICarService> _carService = new();
    private readonly Mock<IUnitOfWork> _uow        = new();
    private CarCatalogQuery? _lastQuery;
    private int _lastPage;

    public CarControllerTests()
    {
        _carService.Setup(s => s.GetAllCategoriesAsync()).ReturnsAsync(Array.Empty<Category>());
        _carService.Setup(s => s.GetCitiesAsync()).ReturnsAsync(new[] { "Київ", "Львів" });
        ReturnCars();
    }

    private CarController CreateSut() => new(_carService.Object, _uow.Object);

    /// <summary>Сервіс повертає ці авто (одна сторінка) і запам'ятовує отриманий запит.</summary>
    private void ReturnCars(int totalCount = -1, int page = 1, params Car[] cars) =>
        _carService
            .Setup(s => s.SearchCatalogAsync(It.IsAny<CarCatalogQuery>(), It.IsAny<int>(), It.IsAny<int>()))
            .Callback<CarCatalogQuery, int, int>((q, p, _) => { _lastQuery = q; _lastPage = p; })
            .ReturnsAsync(new PagedResult<Car>(cars, totalCount < 0 ? cars.Length : totalCount, page));

    private static Car MakeCar(int id, string brand, string model) =>
        new()
        {
            Id           = id,
            Brand        = brand,
            Model        = model,
            Year         = 2023,
            LicensePlate = $"AA{id:D4}BB",
            CategoryId   = 1,
            Category     = new Category { Id = 1, Name = "Sedan" },
            Status       = CarStatus.Available,
            PricePerDay  = 1000m,
            Location     = "Kyiv",
            Seats        = 5,
            FuelType     = FuelType.Petrol,
            Transmission = Transmission.Automatic
        };

    private static CarCatalogViewModel ModelOf(IActionResult result) =>
        result.Should().BeOfType<ViewResult>().Subject
              .Model.Should().BeOfType<CarCatalogViewModel>().Subject;

    // =========================================================================
    //  Index — фільтри
    // =========================================================================

    [Fact]
    public async Task Index_WhenNoFilters_ReturnsViewWithCarsCitiesAndEmptyQuery()
    {
        ReturnCars(cars: [MakeCar(1, "Toyota", "Camry"), MakeCar(2, "Honda", "Civic")]);

        var vm = ModelOf(await CreateSut().Index(null));

        vm.Cars.TotalCount.Should().Be(2);
        vm.Cars.PageIndex.Should().Be(1);
        vm.Cities.Should().Equal("Київ", "Львів");
        vm.RentalDays.Should().BeNull();
        vm.DateErrorKey.Should().BeNull();
        vm.Filter.IsFiltered.Should().BeFalse();
        _lastQuery.Should().BeEquivalentTo(new CarCatalogQuery());
        _lastPage.Should().Be(1);
    }

    [Fact]
    public async Task Index_PassesEveryFilterToService()
    {
        var filter = new CarCatalogFilter
        {
            Search       = "toyota",
            CategoryId   = 2,
            City         = "Львів",
            Transmission = Transmission.Automatic,
            FuelType     = FuelType.Hybrid,
            MinSeats     = 7,
            MaxPrice     = 2000m,
            Sort         = CarSort.PriceDesc,
            Page         = 3
        };

        var vm = ModelOf(await CreateSut().Index(filter));

        _lastQuery.Should().BeEquivalentTo(new CarCatalogQuery
        {
            Search         = "toyota",
            CategoryId     = 2,
            City           = "Львів",
            Transmission   = Transmission.Automatic,
            FuelType       = FuelType.Hybrid,
            MinSeats       = 7,
            MaxPricePerDay = 2000m,
            Sort           = CarSort.PriceDesc
        });
        _lastPage.Should().Be(3);
        vm.Filter.Should().BeSameAs(filter);
    }

    [Fact]
    public async Task Index_UsesPageReturnedByService()
    {
        ReturnCars(totalCount: 13, page: 3, cars: [MakeCar(13, "Kia", "Ceed")]);

        var vm = ModelOf(await CreateSut().Index(new CarCatalogFilter { Page = 99 }));

        vm.Cars.PageIndex.Should().Be(3);
        vm.Cars.TotalPages.Should().Be(3); // 13 авто по 6 на сторінку
        vm.Cars.Should().ContainSingle(c => c.Id == 13);
    }

    // =========================================================================
    //  Index — дати
    // =========================================================================

    [Fact]
    public async Task Index_WhenDatesValid_FiltersByPeriodAndShowsRentalDays()
    {
        var filter = new CarCatalogFilter { PickupDate = Today.AddDays(5), ReturnDate = Today.AddDays(8) };

        var vm = ModelOf(await CreateSut().Index(filter));

        _lastQuery!.PickupDate.Should().Be(Today.AddDays(5));
        _lastQuery.ReturnDate.Should().Be(Today.AddDays(8));
        vm.RentalDays.Should().Be(3);
        vm.DateErrorKey.Should().BeNull();
    }

    [Fact]
    public async Task Index_WhenOnlyPickupDate_IgnoresDatesAndAsksForBoth()
    {
        var vm = ModelOf(await CreateSut().Index(new CarCatalogFilter { PickupDate = Today.AddDays(5) }));

        _lastQuery!.PickupDate.Should().BeNull();
        _lastQuery.ReturnDate.Should().BeNull();
        vm.RentalDays.Should().BeNull();
        vm.DateErrorKey.Should().Be("Car_DatesBoth");
    }

    [Fact]
    public async Task Index_WhenDatesBreakBookingRules_IgnoresDates()
    {
        var filter = new CarCatalogFilter { PickupDate = Today.AddDays(8), ReturnDate = Today.AddDays(5) };

        var vm = ModelOf(await CreateSut().Index(filter));

        _lastQuery!.PickupDate.Should().BeNull();
        vm.RentalDays.Should().BeNull();
        vm.DateErrorKey.Should().Be("Car_DatesInvalid");
    }

    [Theory]
    [InlineData(-1, 2)]   // отримання в минулому
    [InlineData(3, 3)]    // повернення в той самий день
    [InlineData(1, 32)]   // довше 30 днів
    [InlineData(61, 63)]  // більш ніж за 60 днів наперед
    public void ResolvePeriod_WhenRulesBroken_ReturnsInvalid(int pickupOffset, int returnOffset)
    {
        var (pickup, ret, error) = CarController.ResolvePeriod(Today.AddDays(pickupOffset), Today.AddDays(returnOffset), Today);

        pickup.Should().BeNull();
        ret.Should().BeNull();
        error.Should().Be("Car_DatesInvalid");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 30)]
    [InlineData(60, 61)]
    public void ResolvePeriod_WithinRules_ReturnsDates(int pickupOffset, int returnOffset)
    {
        var (pickup, ret, error) = CarController.ResolvePeriod(Today.AddDays(pickupOffset), Today.AddDays(returnOffset), Today);

        pickup.Should().Be(Today.AddDays(pickupOffset));
        ret.Should().Be(Today.AddDays(returnOffset));
        error.Should().BeNull();
    }

    [Fact]
    public void ResolvePeriod_WithoutDates_ReturnsNoPeriodAndNoError()
    {
        CarController.ResolvePeriod(null, null, Today).Should().Be(((DateTime?)null, (DateTime?)null, (string?)null));
    }

    // =========================================================================
    //  CarCatalogFilter — посилання зі збереженими фільтрами
    // =========================================================================

    [Fact]
    public void Filter_ToRouteValues_KeepsOnlySetFiltersWithoutPage()
    {
        var filter = new CarCatalogFilter
        {
            Search     = "  rav4 ",
            City       = "",
            PickupDate = new DateTime(2026, 12, 1),
            MaxPrice   = 1500m,
            Sort       = CarSort.Newest,
            Page       = 4
        };

        filter.ToRouteValues().Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["search"]     = "rav4",
            ["pickupDate"] = "2026-12-01",
            ["maxPrice"]   = "1500",
            ["sort"]       = "Newest"
        });
        filter.IsFiltered.Should().BeTrue();
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
