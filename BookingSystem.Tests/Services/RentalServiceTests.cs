using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services;
using BookingSystem.Services.Dtos;
using BookingSystem.Services.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace BookingSystem.Tests.Services;

/// <summary>
/// Unit-тести для <see cref="RentalService"/>.
///
/// Підхід: всі зовнішні залежності замінені Moq-стабами/моками.
/// Тестується лише бізнес-логіка сервісного шару в ізоляції від EF та email.
/// </summary>
public class RentalServiceTests
{
    // =========================================================================
    //  Поля та інфраструктура
    // =========================================================================

    private readonly Mock<IRentalRepository> _rentalRepo = new();
    private readonly Mock<ICarRepository>    _carRepo    = new();
    private readonly Mock<IUnitOfWork>       _uow        = new();
    private readonly Mock<IEmailService>     _email      = new();
    private readonly Mock<ILogger<RentalService>> _logger = new();

    private const string UserId = "user-42";
    private const int    CarId  = 7;

    public RentalServiceTests()
    {
        // Прив'язуємо репозиторії до UoW один раз для всіх тестів
        _uow.Setup(u => u.Rentals).Returns(_rentalRepo.Object);
        _uow.Setup(u => u.Cars).Returns(_carRepo.Object);
        _uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private RentalService CreateSut() =>
        new(_uow.Object, _email.Object, _logger.Object);

    // ─── Допоміжні фабрики ────────────────────────────────────────────────────

    /// Валідне DTO з датою пікапу через 2 дні, поверненням через 5 днів.
    private static CreateRentalDto ValidDto(int days = 3) =>
        new(CarId,
            DateTime.UtcNow.Date.AddDays(2),
            DateTime.UtcNow.Date.AddDays(2 + days),
            Notes: null);

    private static Car MakeAvailableCar(decimal pricePerDay = 1000m) =>
        new()
        {
            Id           = CarId,
            Brand        = "Toyota",
            Model        = "Camry",
            Year         = 2023,
            LicensePlate = "AA0007BB",
            CategoryId   = 1,
            Category     = new Category { Id = 1, Name = "Sedan" },
            Status       = CarStatus.Available,
            PricePerDay  = pricePerDay,
            Location     = "Kyiv",
            Seats        = 5,
            FuelType     = FuelType.Petrol,
            Transmission = Transmission.Automatic
        };

    private static Rental MakeFullRental(CreateRentalDto dto) => new()
    {
        Id         = 1,
        UserId     = UserId,
        CarId      = dto.CarId,
        PickupDate = dto.PickupDate,
        ReturnDate = dto.ReturnDate,
        Status     = RentalStatus.Pending,
        User       = new ApplicationUser
        {
            Id        = UserId,
            Email     = "user@test.com",
            FirstName = "Ivan",
            LastName  = "Petrenko"
        },
        Car = MakeAvailableCar()
    };

    /// Налаштовує «щасливий шлях» для CreateRentalAsync (всі перевірки проходять).
    private void SetupHappyPath(CreateRentalDto dto, Car? car = null)
    {
        car ??= MakeAvailableCar();

        _carRepo.Setup(r => r.GetWithCategoryAsync(dto.CarId))
                .ReturnsAsync(car);

        _rentalRepo.Setup(r => r.GetActiveRentalCountAsync(UserId))
                   .ReturnsAsync(0);

        _rentalRepo.Setup(r => r.HasOverlapAsync(
                       dto.CarId, dto.PickupDate, dto.ReturnDate, null))
                   .ReturnsAsync(false);

        _rentalRepo.Setup(r => r.AddAsync(It.IsAny<Rental>()))
                   .Returns(Task.CompletedTask);

        _rentalRepo.Setup(r => r.GetWithDetailsAsync(It.IsAny<int>()))
                   .ReturnsAsync(MakeFullRental(dto));
    }

    // =========================================================================
    //  CreateRentalAsync — валідація дат
    // =========================================================================

    [Fact]
    public async Task CreateRentalAsync_WhenPickupDateIsInPast_ReturnsFailure()
    {
        var dto = new CreateRentalDto(CarId,
            PickupDate:  DateTime.UtcNow.Date.AddDays(-1),
            ReturnDate:  DateTime.UtcNow.Date.AddDays(2),
            Notes: null);

        var result = await CreateSut().CreateRentalAsync(UserId, dto);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("past");
    }

    [Fact]
    public async Task CreateRentalAsync_WhenReturnDateIsNotAfterPickupDate_ReturnsFailure()
    {
        var pickup = DateTime.UtcNow.Date.AddDays(2);
        var dto    = new CreateRentalDto(CarId, pickup, pickup, Notes: null); // однакові дати

        var result = await CreateSut().CreateRentalAsync(UserId, dto);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("return date");
    }

    [Fact]
    public async Task CreateRentalAsync_WhenRentalDurationExceedsMaxDays_ReturnsFailure()
    {
        var dto = new CreateRentalDto(CarId,
            PickupDate: DateTime.UtcNow.Date.AddDays(1),
            ReturnDate: DateTime.UtcNow.Date.AddDays(1 + 31), // 31 день > MaxRentalDays(30)
            Notes: null);

        var result = await CreateSut().CreateRentalAsync(UserId, dto);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("30");
    }

    [Fact]
    public async Task CreateRentalAsync_WhenPickupDateIsTooFarInAdvance_ReturnsFailure()
    {
        var dto = new CreateRentalDto(CarId,
            PickupDate: DateTime.UtcNow.Date.AddDays(61), // > MaxAdvanceBookingDays(60)
            ReturnDate: DateTime.UtcNow.Date.AddDays(64),
            Notes: null);

        var result = await CreateSut().CreateRentalAsync(UserId, dto);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("60");
    }

    // =========================================================================
    //  CreateRentalAsync — бізнес-правила
    // =========================================================================

    [Fact]
    public async Task CreateRentalAsync_WhenCarDoesNotExist_ReturnsFailure()
    {
        _carRepo.Setup(r => r.GetWithCategoryAsync(CarId))
                .ReturnsAsync((Car?)null);

        var result = await CreateSut().CreateRentalAsync(UserId, ValidDto());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("does not exist");
    }

    [Fact]
    public async Task CreateRentalAsync_WhenCarIsNotAvailable_ReturnsFailure()
    {
        var car = MakeAvailableCar();
        car.Status = CarStatus.Rented;

        _carRepo.Setup(r => r.GetWithCategoryAsync(CarId)).ReturnsAsync(car);

        var result = await CreateSut().CreateRentalAsync(UserId, ValidDto());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("rented");
    }

    [Fact]
    public async Task CreateRentalAsync_WhenUserAlreadyHasMaxActiveRentals_ReturnsFailure()
    {
        _carRepo.Setup(r => r.GetWithCategoryAsync(CarId))
                .ReturnsAsync(MakeAvailableCar());

        _rentalRepo.Setup(r => r.GetActiveRentalCountAsync(UserId))
                   .ReturnsAsync(3); // == MaxActiveRentalsPerUser

        var result = await CreateSut().CreateRentalAsync(UserId, ValidDto());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("3 active rentals");
    }

    [Fact]
    public async Task CreateRentalAsync_WhenDatesOverlapExistingRental_ReturnsFailure()
    {
        var dto = ValidDto();
        _carRepo.Setup(r => r.GetWithCategoryAsync(dto.CarId))
                .ReturnsAsync(MakeAvailableCar());

        _rentalRepo.Setup(r => r.GetActiveRentalCountAsync(UserId))
                   .ReturnsAsync(0);

        _rentalRepo.Setup(r => r.HasOverlapAsync(
                       dto.CarId, dto.PickupDate, dto.ReturnDate, null))
                   .ReturnsAsync(true);

        var result = await CreateSut().CreateRentalAsync(UserId, dto);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already rented");
    }

    // =========================================================================
    //  CreateRentalAsync — успішний шлях
    // =========================================================================

    [Fact]
    public async Task CreateRentalAsync_WhenAllValidationsPass_CalculatesCorrectTotalPrice()
    {
        const decimal pricePerDay = 800m;
        const int     rentalDays  = 5;

        var dto = ValidDto(days: rentalDays);
        SetupHappyPath(dto, MakeAvailableCar(pricePerDay));

        Rental? captured = null;
        _rentalRepo.Setup(r => r.AddAsync(It.IsAny<Rental>()))
                   .Callback<Rental>(r => captured = r)
                   .Returns(Task.CompletedTask);

        await CreateSut().CreateRentalAsync(UserId, dto);

        captured.Should().NotBeNull();
        captured!.TotalPrice.Should().Be(pricePerDay * rentalDays);
    }

    [Fact]
    public async Task CreateRentalAsync_WhenAllValidationsPass_ReturnsRentalWithPendingStatus()
    {
        var dto = ValidDto();
        SetupHappyPath(dto);

        Rental? captured = null;
        _rentalRepo.Setup(r => r.AddAsync(It.IsAny<Rental>()))
                   .Callback<Rental>(r => captured = r)
                   .Returns(Task.CompletedTask);

        var result = await CreateSut().CreateRentalAsync(UserId, dto);

        result.IsSuccess.Should().BeTrue();
        captured!.Status.Should().Be(RentalStatus.Pending);
        captured.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task CreateRentalAsync_WhenEmailThrows_StillReturnsSuccess()
    {
        var dto = ValidDto();
        SetupHappyPath(dto);

        _email.Setup(e => e.SendRentalCreatedAsync(
                  It.IsAny<string>(), It.IsAny<string>(),
                  It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
              .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        var result = await CreateSut().CreateRentalAsync(UserId, dto);

        // Email-помилка не повинна скасовувати бронювання
        result.IsSuccess.Should().BeTrue();
    }

    // =========================================================================
    //  CancelRentalAsync
    // =========================================================================

    [Fact]
    public async Task CancelRentalAsync_WhenRentalNotFound_ReturnsFailure()
    {
        _rentalRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
                   .ReturnsAsync((Rental?)null);

        var result = await CreateSut().CancelRentalAsync(99, UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task CancelRentalAsync_WhenUserDoesNotOwnRental_ReturnsFailure()
    {
        var rental = new Rental { Id = 1, UserId = "other-user", Status = RentalStatus.Pending };
        _rentalRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(rental);

        var result = await CreateSut().CancelRentalAsync(1, UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("own rentals");
    }

    [Theory]
    [InlineData(RentalStatus.Cancelled)]
    [InlineData(RentalStatus.Rejected)]
    public async Task CancelRentalAsync_WhenRentalIsAlreadyTerminated_ReturnsFailure(RentalStatus status)
    {
        var rental = new Rental { Id = 1, UserId = UserId, Status = status };
        _rentalRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(rental);

        var result = await CreateSut().CancelRentalAsync(1, UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cancelled or rejected");
    }

    [Fact]
    public async Task CancelRentalAsync_WhenPickupDateAlreadyPassed_ReturnsFailure()
    {
        var rental = new Rental
        {
            Id         = 1,
            UserId     = UserId,
            Status     = RentalStatus.Confirmed,
            PickupDate = DateTime.UtcNow.AddDays(-1) // вже почалась
        };
        _rentalRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(rental);

        var result = await CreateSut().CancelRentalAsync(1, UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already started");
    }

    [Fact]
    public async Task CancelRentalAsync_WhenPendingRentalWithFuturePickup_SetsCancelledStatus()
    {
        var rental = new Rental
        {
            Id         = 1,
            UserId     = UserId,
            Status     = RentalStatus.Pending,
            PickupDate = DateTime.UtcNow.AddDays(3),
            ReturnDate = DateTime.UtcNow.AddDays(6),
            Car        = MakeAvailableCar(),
            User       = new ApplicationUser
                { Id = UserId, Email = "u@test.com", FirstName = "I", LastName = "P" }
        };

        _rentalRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(rental);
        _rentalRepo.Setup(r => r.GetWithDetailsAsync(1)).ReturnsAsync(rental);

        var result = await CreateSut().CancelRentalAsync(1, UserId);

        result.IsSuccess.Should().BeTrue();
        rental.Status.Should().Be(RentalStatus.Cancelled);
        rental.CancelledAt.Should().NotBeNull();

        _rentalRepo.Verify(r => r.Update(rental), Times.Once());
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    // =========================================================================
    //  ConfirmRentalAsync
    // =========================================================================

    [Fact]
    public async Task ConfirmRentalAsync_WhenRentalNotFound_ReturnsFailure()
    {
        _rentalRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
                   .ReturnsAsync((Rental?)null);

        var result = await CreateSut().ConfirmRentalAsync(99, null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Theory]
    [InlineData(RentalStatus.Confirmed)]
    [InlineData(RentalStatus.Cancelled)]
    [InlineData(RentalStatus.Rejected)]
    public async Task ConfirmRentalAsync_WhenRentalIsNotPending_ReturnsFailure(RentalStatus status)
    {
        var rental = new Rental { Id = 1, Status = status };
        _rentalRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(rental);

        var result = await CreateSut().ConfirmRentalAsync(1, null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Pending");
    }

    [Fact]
    public async Task ConfirmRentalAsync_WhenRentalIsPending_SetsConfirmedStatusAndSavesNote()
    {
        const string note   = "Approved by manager";
        var          rental = new Rental
        {
            Id         = 1,
            Status     = RentalStatus.Pending,
            PickupDate = DateTime.UtcNow.AddDays(2),
            ReturnDate = DateTime.UtcNow.AddDays(5),
            User       = new ApplicationUser
                { Id = "u", Email = "u@test.com", FirstName = "I", LastName = "P" },
            Car = MakeAvailableCar()
        };

        _rentalRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(rental);
        _rentalRepo.Setup(r => r.GetWithDetailsAsync(1)).ReturnsAsync(rental);

        var result = await CreateSut().ConfirmRentalAsync(1, note);

        result.IsSuccess.Should().BeTrue();
        rental.Status.Should().Be(RentalStatus.Confirmed);
        rental.AdminNote.Should().Be(note);

        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    // =========================================================================
    //  RejectRentalAsync
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RejectRentalAsync_WhenNoteIsNullOrWhitespace_ReturnsFailure(string? note)
    {
        var result = await CreateSut().RejectRentalAsync(1, note!);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("rejection reason");
    }

    [Theory]
    [InlineData(RentalStatus.Confirmed)]
    [InlineData(RentalStatus.Cancelled)]
    [InlineData(RentalStatus.Rejected)]
    public async Task RejectRentalAsync_WhenRentalIsNotPending_ReturnsFailure(RentalStatus status)
    {
        var rental = new Rental { Id = 1, Status = status };
        _rentalRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(rental);

        var result = await CreateSut().RejectRentalAsync(1, "reason");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Pending");
    }

    [Fact]
    public async Task RejectRentalAsync_WhenRentalIsPending_SetsRejectedStatusAndSavesNote()
    {
        const string note   = "Documents not valid";
        var          rental = new Rental
        {
            Id     = 1,
            Status = RentalStatus.Pending,
            User   = new ApplicationUser
                { Id = "u", Email = "u@test.com", FirstName = "I", LastName = "P" },
            Car    = MakeAvailableCar()
        };

        _rentalRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(rental);
        _rentalRepo.Setup(r => r.GetWithDetailsAsync(1)).ReturnsAsync(rental);

        var result = await CreateSut().RejectRentalAsync(1, note);

        result.IsSuccess.Should().BeTrue();
        rental.Status.Should().Be(RentalStatus.Rejected);
        rental.AdminNote.Should().Be(note);

        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once());
    }
}
