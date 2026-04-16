using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace BookingSystem.Tests.Repositories;

/// <summary>
/// Unit-тести для <see cref="RentalRepository"/>.
///
/// Підхід: EF Core InMemory — ізольована БД на кожен тест (унікальний Guid),
/// без seed-даних, із повним контролем тестових даних.
/// Moq-стаби використовуються для демонстрації ізоляції через <see cref="IRentalRepository"/>.
/// </summary>
public class RentalRepositoryTests
{
    // =========================================================================
    //  Допоміжні методи
    // =========================================================================

    private static Category MakeCategory() =>
        new() { Id = 1, Name = "Sedan" };

    private static Car MakeCar(Category category) =>
        new()
        {
            Id           = 1,
            Brand        = "Toyota",
            Model        = "Camry",
            Year         = 2023,
            LicensePlate = "AA1234BB",
            CategoryId   = category.Id,
            Status       = CarStatus.Available,
            PricePerDay  = 1200m,
            Location     = "Kyiv",
            Seats        = 5,
            FuelType     = FuelType.Petrol,
            Transmission = Transmission.Automatic
        };

    private static ApplicationUser MakeUser(string id, string email) =>
        new()
        {
            Id                 = id,
            Email              = email,
            UserName           = email,
            NormalizedEmail    = email.ToUpper(),
            NormalizedUserName = email.ToUpper(),
            FirstName          = "Test",
            LastName           = "User"
        };

    private static Rental MakeRental(
        int          id,
        string       userId,
        int          carId,
        DateTime     pickupDate,
        DateTime     returnDate,
        RentalStatus status     = RentalStatus.Pending,
        decimal      totalPrice = 1200m) =>
        new()
        {
            Id         = id,
            UserId     = userId,
            CarId      = carId,
            PickupDate = pickupDate,
            ReturnDate = returnDate,
            Status     = status,
            TotalPrice = totalPrice,
            CreatedAt  = DateTime.UtcNow
        };

    // =========================================================================
    //  GetUserRentalsAsync — InMemory
    // =========================================================================

    [Fact]
    public async Task GetUserRentalsAsync_WhenUserHasTwoRentals_ReturnsBothRentals()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var user     = MakeUser("user-001", "alice@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.Add(user);
        db.Rentals.AddRange(
            MakeRental(1, user.Id, car.Id, DateTime.Today.AddDays(1),  DateTime.Today.AddDays(3)),
            MakeRental(2, user.Id, car.Id, DateTime.Today.AddDays(10), DateTime.Today.AddDays(12)));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act
        var result = await sut.GetUserRentalsAsync(user.Id);

        // Assert
        result.Should().HaveCount(2)
              .And.OnlyContain(r => r.UserId == user.Id);
    }

    [Fact]
    public async Task GetUserRentalsAsync_WhenTwoUsersBothHaveRentals_ReturnsOnlyRequestedUsersRentals()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var alice    = MakeUser("user-001", "alice@test.com");
        var bob      = MakeUser("user-002", "bob@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.AddRange(alice, bob);
        db.Rentals.AddRange(
            MakeRental(1, alice.Id, car.Id, DateTime.Today.AddDays(1), DateTime.Today.AddDays(3)),
            MakeRental(2, bob.Id,   car.Id, DateTime.Today.AddDays(5), DateTime.Today.AddDays(7)));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act
        var result = (await sut.GetUserRentalsAsync(alice.Id)).ToList();

        // Assert
        result.Should().HaveCount(1);
        result.Single().UserId.Should().Be(alice.Id);
    }

    [Fact]
    public async Task GetUserRentalsAsync_WhenUserHasNoRentals_ReturnsEmptyCollection()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var sut = new RentalRepository(db);

        // Act
        var result = await sut.GetUserRentalsAsync("nonexistent-user");

        // Assert
        result.Should().BeEmpty();
    }

    // =========================================================================
    //  GetAllWithDetailsAsync — InMemory
    // =========================================================================

    [Fact]
    public async Task GetAllWithDetailsAsync_WhenTwoRentalsExist_ReturnsBothWithCarAndUserLoaded()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var user     = MakeUser("user-001", "alice@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.Add(user);
        db.Rentals.AddRange(
            MakeRental(1, user.Id, car.Id, DateTime.Today.AddDays(1), DateTime.Today.AddDays(3)),
            MakeRental(2, user.Id, car.Id, DateTime.Today.AddDays(5), DateTime.Today.AddDays(7)));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act
        var result = (await sut.GetAllWithDetailsAsync()).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(r =>
        {
            r.Car.Should().NotBeNull();
            r.User.Should().NotBeNull();
        });
    }

    // =========================================================================
    //  GetWithDetailsAsync — InMemory
    // =========================================================================

    [Fact]
    public async Task GetWithDetailsAsync_WhenRentalExists_ReturnsRentalWithAllDetails()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var user     = MakeUser("user-001", "alice@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.Add(user);
        db.Rentals.Add(MakeRental(1, user.Id, car.Id,
            DateTime.Today.AddDays(1), DateTime.Today.AddDays(3)));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act
        var result = await sut.GetWithDetailsAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Car.Should().NotBeNull();
        result.Car.Brand.Should().Be("Toyota");
        result.User.Should().NotBeNull();
    }

    [Fact]
    public async Task GetWithDetailsAsync_WhenRentalDoesNotExist_ReturnsNull()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var sut = new RentalRepository(db);

        // Act
        var result = await sut.GetWithDetailsAsync(999);

        // Assert
        result.Should().BeNull();
    }

    // =========================================================================
    //  HasOverlapAsync — InMemory (критична бізнес-логіка)
    // =========================================================================

    [Fact]
    public async Task HasOverlapAsync_WhenNewPeriodFullyOverlapsExistingPending_ReturnsTrue()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var user     = MakeUser("user-001", "alice@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.Add(user);

        // Існуюча оренда: 5–10 травня
        db.Rentals.Add(MakeRental(1, user.Id, car.Id,
            new DateTime(2026, 5, 5), new DateTime(2026, 5, 10),
            RentalStatus.Pending));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act — нова оренда: 3–12 травня (повністю охоплює існуючу)
        var result = await sut.HasOverlapAsync(
            car.Id,
            new DateTime(2026, 5, 3),
            new DateTime(2026, 5, 12));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasOverlapAsync_WhenNewPeriodPartiallyOverlapsExistingConfirmed_ReturnsTrue()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var user     = MakeUser("user-001", "alice@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.Add(user);

        // Існуюча оренда: 5–10 травня (Confirmed)
        db.Rentals.Add(MakeRental(1, user.Id, car.Id,
            new DateTime(2026, 5, 5), new DateTime(2026, 5, 10),
            RentalStatus.Confirmed));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act — нова оренда: 8–15 травня (часткове перекриття)
        var result = await sut.HasOverlapAsync(
            car.Id,
            new DateTime(2026, 5, 8),
            new DateTime(2026, 5, 15));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasOverlapAsync_WhenNewPeriodComesAfterExistingRental_ReturnsFalse()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var user     = MakeUser("user-001", "alice@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.Add(user);

        // Існуюча оренда: 1–5 травня
        db.Rentals.Add(MakeRental(1, user.Id, car.Id,
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 5),
            RentalStatus.Confirmed));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act — нова оренда: 6–10 травня (не перетинається)
        var result = await sut.HasOverlapAsync(
            car.Id,
            new DateTime(2026, 5, 6),
            new DateTime(2026, 5, 10));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasOverlapAsync_WhenExistingRentalIsCancelled_ReturnsFalse()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var user     = MakeUser("user-001", "alice@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.Add(user);

        // Скасована оренда не повинна блокувати нову
        db.Rentals.Add(MakeRental(1, user.Id, car.Id,
            new DateTime(2026, 5, 5), new DateTime(2026, 5, 10),
            RentalStatus.Cancelled));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act
        var result = await sut.HasOverlapAsync(
            car.Id,
            new DateTime(2026, 5, 3),
            new DateTime(2026, 5, 12));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasOverlapAsync_WhenExistingRentalIsRejected_ReturnsFalse()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var user     = MakeUser("user-001", "alice@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.Add(user);

        db.Rentals.Add(MakeRental(1, user.Id, car.Id,
            new DateTime(2026, 5, 5), new DateTime(2026, 5, 10),
            RentalStatus.Rejected));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act
        var result = await sut.HasOverlapAsync(
            car.Id,
            new DateTime(2026, 5, 3),
            new DateTime(2026, 5, 12));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasOverlapAsync_WhenExcludeRentalIdProvided_IgnoresThatRental()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var user     = MakeUser("user-001", "alice@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.Add(user);

        // Оренда з Id=1 — та сама, що редагується
        db.Rentals.Add(MakeRental(1, user.Id, car.Id,
            new DateTime(2026, 5, 5), new DateTime(2026, 5, 10),
            RentalStatus.Confirmed));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act — виключаємо rentalId=1, тому перетину нема
        var result = await sut.HasOverlapAsync(
            car.Id,
            new DateTime(2026, 5, 3),
            new DateTime(2026, 5, 12),
            excludeRentalId: 1);

        // Assert
        result.Should().BeFalse();
    }

    // =========================================================================
    //  GetActiveRentalCountAsync — InMemory
    // =========================================================================

    [Fact]
    public async Task GetActiveRentalCountAsync_WhenUserHasTwoPendingAndOneConfirmed_ReturnsThree()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var user     = MakeUser("user-001", "alice@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.Add(user);
        db.Rentals.AddRange(
            MakeRental(1, user.Id, car.Id, DateTime.Today.AddDays(1),  DateTime.Today.AddDays(3),  RentalStatus.Pending),
            MakeRental(2, user.Id, car.Id, DateTime.Today.AddDays(5),  DateTime.Today.AddDays(7),  RentalStatus.Pending),
            MakeRental(3, user.Id, car.Id, DateTime.Today.AddDays(10), DateTime.Today.AddDays(12), RentalStatus.Confirmed),
            MakeRental(4, user.Id, car.Id, DateTime.Today.AddDays(15), DateTime.Today.AddDays(17), RentalStatus.Cancelled),
            MakeRental(5, user.Id, car.Id, DateTime.Today.AddDays(20), DateTime.Today.AddDays(22), RentalStatus.Rejected));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act
        var count = await sut.GetActiveRentalCountAsync(user.Id);

        // Assert — тільки Pending та Confirmed вважаються активними
        count.Should().Be(3);
    }

    [Fact]
    public async Task GetActiveRentalCountAsync_WhenAllRentalsCancelledOrRejected_ReturnsZero()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var user     = MakeUser("user-001", "alice@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.Add(user);
        db.Rentals.AddRange(
            MakeRental(1, user.Id, car.Id, DateTime.Today.AddDays(1), DateTime.Today.AddDays(3), RentalStatus.Cancelled),
            MakeRental(2, user.Id, car.Id, DateTime.Today.AddDays(5), DateTime.Today.AddDays(7), RentalStatus.Rejected));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act
        var count = await sut.GetActiveRentalCountAsync(user.Id);

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetActiveRentalCountAsync_WhenUserHasNoRentals_ReturnsZero()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var sut = new RentalRepository(db);

        // Act
        var count = await sut.GetActiveRentalCountAsync("nonexistent-user");

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetActiveRentalCountAsync_WhenCountingForSpecificUser_DoesNotCountOtherUsersRentals()
    {
        // Arrange
        await using var db = TestDbContextFactory.Create();
        var category = MakeCategory();
        var car      = MakeCar(category);
        var alice    = MakeUser("user-001", "alice@test.com");
        var bob      = MakeUser("user-002", "bob@test.com");
        db.Categories.Add(category);
        db.Cars.Add(car);
        db.Users.AddRange(alice, bob);
        db.Rentals.AddRange(
            MakeRental(1, alice.Id, car.Id, DateTime.Today.AddDays(1), DateTime.Today.AddDays(3), RentalStatus.Pending),
            MakeRental(2, alice.Id, car.Id, DateTime.Today.AddDays(5), DateTime.Today.AddDays(7), RentalStatus.Confirmed),
            MakeRental(3, bob.Id,   car.Id, DateTime.Today.AddDays(10),DateTime.Today.AddDays(12),RentalStatus.Pending));
        await db.SaveChangesAsync();

        var sut = new RentalRepository(db);

        // Act
        var aliceCount = await sut.GetActiveRentalCountAsync(alice.Id);
        var bobCount   = await sut.GetActiveRentalCountAsync(bob.Id);

        // Assert
        aliceCount.Should().Be(2);
        bobCount.Should().Be(1);
    }

    // =========================================================================
    //  Moq — демонстрація стабів і моків IRentalRepository
    // =========================================================================

    [Fact]
    public async Task HasOverlapAsync_WhenRepositoryStubbed_ReturnsStubbedValue()
    {
        // Arrange — stub: ізолюємо тест від реальної БД
        var mockRepo = new Mock<IRentalRepository>();
        mockRepo
            .Setup(r => r.HasOverlapAsync(
                It.IsAny<int>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<int?>()))
            .ReturnsAsync(true);

        // Act
        var result = await mockRepo.Object.HasOverlapAsync(
            carId: 1,
            pickupDate: DateTime.Today.AddDays(1),
            returnDate: DateTime.Today.AddDays(3));

        // Assert
        result.Should().BeTrue();
        mockRepo.Verify(
            r => r.HasOverlapAsync(1, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null),
            Times.Once());
    }

    [Fact]
    public async Task GetActiveRentalCountAsync_WhenRepositoryStubbed_ReturnsStubbedCount()
    {
        // Arrange — stub: повертає максимальну кількість активних оренд (3)
        const int maxRentals = 3;
        var mockRepo = new Mock<IRentalRepository>();
        mockRepo
            .Setup(r => r.GetActiveRentalCountAsync(It.IsAny<string>()))
            .ReturnsAsync(maxRentals);

        // Act
        var count = await mockRepo.Object.GetActiveRentalCountAsync("any-user");

        // Assert — перевіряємо значення та взаємодію
        count.Should().Be(maxRentals);
        mockRepo.Verify(r => r.GetActiveRentalCountAsync("any-user"), Times.Once());
    }
}
