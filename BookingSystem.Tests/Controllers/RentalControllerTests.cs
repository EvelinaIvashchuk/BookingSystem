using System.Security.Claims;
using AutoMapper;
using BookingSystem.Controllers;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Resources;
using BookingSystem.Services;
using BookingSystem.Services.Dtos;
using BookingSystem.Services.Interfaces;
using BookingSystem.ViewModels;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;

namespace BookingSystem.Tests.Controllers;

/// <summary>
/// Unit-тести для <see cref="RentalController"/>.
///
/// Підхід: контролер інстанціюється напряму.
/// Ідентичність користувача підставляється через ClaimsPrincipal у HttpContext.
/// TempData реалізований через справжній TempDataDictionary.
/// </summary>
public class RentalControllerTests
{
    // =========================================================================
    //  Поля та інфраструктура
    // =========================================================================

    private readonly Mock<IRentalService>                _rentalService = new();
    private readonly Mock<ICarService>                   _carService    = new();
    private readonly Mock<IMapper>                       _mapper        = new();
    private readonly Mock<IValidator<RentalCreateViewModel>> _validator  = new();
    private readonly Mock<IStringLocalizer<SharedResources>> _localizer  = new();

    private const string UserId = "user-42";

    public RentalControllerTests()
    {
        // За замовчуванням локалізатор повертає ключ як текст
        _localizer
            .Setup(l => l[It.IsAny<string>()])
            .Returns<string>(key => new LocalizedString(key, key));
        _localizer
            .Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()])
            .Returns<string, object[]>((key, args) =>
                new LocalizedString(key, string.Format(key, args)));
    }

    private RentalController CreateSut(string userId = UserId, bool isAdmin = false)
    {
        var sut = new RentalController(
            _rentalService.Object,
            _carService.Object,
            _mapper.Object,
            _validator.Object,
            _localizer.Object);

        sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = MakeUser(userId, isAdmin) }
        };

        sut.TempData = new TempDataDictionary(
            sut.ControllerContext.HttpContext,
            Mock.Of<ITempDataProvider>());

        return sut;
    }

    private static ClaimsPrincipal MakeUser(string userId, bool isAdmin = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static Car MakeAvailableCar(int id = 1) => new()
    {
        Id           = id,
        Brand        = "Toyota",
        Model        = "Camry",
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

    private static Rental MakeRental(string userId, int id = 1) => new()
    {
        Id     = id,
        UserId = userId,
        CarId  = 1,
        Status = RentalStatus.Pending,
        User   = new ApplicationUser
        {
            Id        = userId,
            Email     = "user@test.com",
            FirstName = "Ivan",
            LastName  = "Petrenko"
        },
        Car = MakeAvailableCar()
    };

    // ─── Налаштування валідатора ──────────────────────────────────────────────

    private void SetupValidatorPass()
    {
        // ValidateAsync(vm) викликає IValidator<T>.ValidateAsync(T instance, CancellationToken)
        _validator
            .Setup(v => v.ValidateAsync(
                It.IsAny<RentalCreateViewModel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
    }

    private void SetupValidatorFail(string errorMessage = "Validation error")
    {
        _validator
            .Setup(v => v.ValidateAsync(
                It.IsAny<RentalCreateViewModel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(
                new[] { new ValidationFailure("PickupDate", errorMessage) }));
    }

    // =========================================================================
    //  GET Create
    // =========================================================================

    [Fact]
    public async Task Create_Get_WhenCarDoesNotExist_ReturnsNotFound()
    {
        _carService.Setup(s => s.GetCarByIdAsync(It.IsAny<int>()))
                   .ReturnsAsync((Car?)null);

        var result = await CreateSut().Create(carId: 99, pickupDate: null, returnDate: null);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Create_Get_WhenCarIsNotAvailable_RedirectsToCarDetailsWithError()
    {
        var car = MakeAvailableCar();
        car.Status = CarStatus.Rented;
        _carService.Setup(s => s.GetCarByIdAsync(car.Id)).ReturnsAsync(car);

        var sut    = CreateSut();
        var result = await sut.Create(carId: car.Id, pickupDate: null, returnDate: null);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be("Details");
        redirect.ControllerName.Should().Be("Car");
        redirect.RouteValues!["id"].Should().Be(car.Id);

        sut.TempData.Should().ContainKey("Error");
    }

    [Fact]
    public async Task Create_Get_WhenCarIsAvailable_ReturnsViewWithPopulatedViewModel()
    {
        var car = MakeAvailableCar(id: 5);
        _carService.Setup(s => s.GetCarByIdAsync(5)).ReturnsAsync(car);

        var result = await CreateSut().Create(carId: 5, pickupDate: null, returnDate: null);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        var vm   = view.Model.Should().BeOfType<RentalCreateViewModel>().Subject;

        vm.CarId.Should().Be(5);
        vm.CarName.Should().Be(car.FullName);
        vm.Location.Should().Be(car.Location);
        vm.PricePerDay.Should().Be(car.PricePerDay);
        vm.CategoryName.Should().Be(car.Category.Name);
    }

    // =========================================================================
    //  POST Create
    // =========================================================================

    [Fact]
    public async Task Create_Post_WhenValidationFails_ReturnsViewWithErrors()
    {
        SetupValidatorFail("Pickup date is required.");
        _carService.Setup(s => s.GetCarByIdAsync(It.IsAny<int>()))
                   .ReturnsAsync(MakeAvailableCar());

        var vm     = new RentalCreateViewModel { CarId = 1 };
        var result = await CreateSut().Create(vm);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.Model.Should().Be(vm);
        // Сервіс бронювання не викликається при невалідній формі
        _rentalService.Verify(
            s => s.CreateRentalAsync(It.IsAny<string>(), It.IsAny<CreateRentalDto>()),
            Times.Never());
    }

    [Fact]
    public async Task Create_Post_WhenServiceReturnsFailure_AddsModelError()
    {
        SetupValidatorPass();
        _mapper.Setup(m => m.Map<CreateRentalDto>(It.IsAny<RentalCreateViewModel>()))
               .Returns(new CreateRentalDto(1, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(4), null));
        _rentalService
            .Setup(s => s.CreateRentalAsync(UserId, It.IsAny<CreateRentalDto>()))
            .ReturnsAsync(ServiceResult<Rental>.Fail("Car is not available."));
        _carService.Setup(s => s.GetCarByIdAsync(It.IsAny<int>()))
                   .ReturnsAsync(MakeAvailableCar());

        var vm     = new RentalCreateViewModel { CarId = 1 };
        var sut    = CreateSut();
        var result = await sut.Create(vm);

        result.Should().BeOfType<ViewResult>();
        sut.ModelState.IsValid.Should().BeFalse();
        sut.ModelState[string.Empty]!.Errors
            .Should().ContainSingle(e => e.ErrorMessage == "Car is not available.");
    }

    [Fact]
    public async Task Create_Post_WhenSuccess_SetsSuccessTempDataAndRedirectsToMyRentals()
    {
        var rental = MakeRental(UserId);

        SetupValidatorPass();
        _mapper.Setup(m => m.Map<CreateRentalDto>(It.IsAny<RentalCreateViewModel>()))
               .Returns(new CreateRentalDto(1, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(4), null));
        _rentalService
            .Setup(s => s.CreateRentalAsync(UserId, It.IsAny<CreateRentalDto>()))
            .ReturnsAsync(ServiceResult<Rental>.Ok(rental));

        var sut    = CreateSut();
        var result = await sut.Create(new RentalCreateViewModel { CarId = 1 });

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(RentalController.MyRentals));

        sut.TempData.Should().ContainKey("Success");
    }

    // =========================================================================
    //  Details
    // =========================================================================

    [Fact]
    public async Task Details_WhenRentalNotFound_ReturnsNotFound()
    {
        _rentalService.Setup(s => s.GetRentalByIdAsync(It.IsAny<int>()))
                      .ReturnsAsync((Rental?)null);

        var result = await CreateSut().Details(99);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Details_WhenOwnerAccesses_ReturnsViewWithRental()
    {
        var rental = MakeRental(UserId);
        _rentalService.Setup(s => s.GetRentalByIdAsync(rental.Id)).ReturnsAsync(rental);

        var result = await CreateSut(userId: UserId).Details(rental.Id);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.Model.Should().BeSameAs(rental);
    }

    [Fact]
    public async Task Details_WhenUserDoesNotOwnRentalAndIsNotAdmin_ReturnsForbid()
    {
        var rental = MakeRental(userId: "other-user");
        _rentalService.Setup(s => s.GetRentalByIdAsync(rental.Id)).ReturnsAsync(rental);

        // Поточний користувач — не власник, не адмін
        var result = await CreateSut(userId: UserId, isAdmin: false).Details(rental.Id);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Details_WhenAdminAccessesAnyRental_ReturnsView()
    {
        var rental = MakeRental(userId: "some-other-user");
        _rentalService.Setup(s => s.GetRentalByIdAsync(rental.Id)).ReturnsAsync(rental);

        // Адмін може переглядати будь-яке бронювання
        var result = await CreateSut(userId: "admin-id", isAdmin: true).Details(rental.Id);

        result.Should().BeOfType<ViewResult>();
    }

    // =========================================================================
    //  POST Cancel
    // =========================================================================

    [Fact]
    public async Task Cancel_WhenServiceSucceeds_SetsSuccessTempDataAndRedirects()
    {
        _rentalService
            .Setup(s => s.CancelRentalAsync(1, UserId))
            .ReturnsAsync(ServiceResult.Ok());

        var sut    = CreateSut();
        var result = await sut.Cancel(1);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(RentalController.MyRentals));

        sut.TempData.Should().ContainKey("Success");
        sut.TempData.Should().NotContainKey("Error");
    }

    [Fact]
    public async Task Cancel_WhenServiceFails_SetsErrorTempDataAndRedirects()
    {
        _rentalService
            .Setup(s => s.CancelRentalAsync(1, UserId))
            .ReturnsAsync(ServiceResult.Fail("You can only cancel your own rentals."));

        var sut    = CreateSut();
        var result = await sut.Cancel(1);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(RentalController.MyRentals));

        sut.TempData["Error"].Should().Be("You can only cancel your own rentals.");
        sut.TempData.Should().NotContainKey("Success");
    }

    // =========================================================================
    //  GET MyRentals
    // =========================================================================

    [Fact]
    public async Task MyRentals_ReturnsViewWithPaginatedUserRentals()
    {
        var rentals = Enumerable.Range(1, 5)
            .Select(i => MakeRental(UserId, i))
            .ToList();

        _rentalService.Setup(s => s.GetUserRentalsAsync(UserId))
                      .ReturnsAsync(rentals);

        var result = await CreateSut().MyRentals(page: 1);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        // PaginatedList<Rental> наслідує List<Rental>
        var model = view.Model.Should().BeAssignableTo<IEnumerable<Rental>>().Subject;
        model.Should().HaveCount(5);
    }
}
