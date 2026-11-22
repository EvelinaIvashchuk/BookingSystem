using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Tests.Helpers;
using FluentAssertions;

namespace BookingSystem.Tests.Repositories;

/// <summary>
/// Тести каталогу: <see cref="CarRepository.SearchCatalogAsync"/> і <see cref="CarRepository.GetCitiesAsync"/>
/// на InMemory-базі (реальні EF-запити: фільтри, зайнятість на дати, сортування, сторінки).
/// </summary>
public class CarCatalogSearchTests
{
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static readonly Category Sedan = new() { Id = 1, Name = "Sedan" };
    private static readonly Category Wagon = new() { Id = 2, Name = "Wagon" };

    private static Car MakeCar(
        int          id,
        string       brand,
        string       model,
        int          categoryId   = 1,
        string       location     = "Київ, вул. Хрещатик, 10",
        CarStatus    status       = CarStatus.Available,
        decimal      price        = 1000m,
        int          year         = 2023,
        int          seats        = 5,
        Transmission transmission = Transmission.Automatic,
        FuelType     fuel         = FuelType.Petrol,
        string?      description  = null) =>
        new()
        {
            Id = id, Brand = brand, Model = model, Year = year, LicensePlate = $"AA{id:D4}BB",
            CategoryId = categoryId, Status = status, PricePerDay = price, Location = location,
            Seats = seats, Transmission = transmission, FuelType = fuel, Description = description
        };

    private static async Task<ApplicationDbContext> SeedAsync(params Car[] cars)
    {
        var db = TestDbContextFactory.Create();
        db.Categories.AddRange(new Category { Id = Sedan.Id, Name = Sedan.Name }, new Category { Id = Wagon.Id, Name = Wagon.Name });
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1", FirstName = "Test", LastName = "User" });
        db.Cars.AddRange(cars);
        await db.SaveChangesAsync();
        return db;
    }

    private static Task<PagedResult<Car>> Search(ApplicationDbContext db, CarCatalogQuery query, int page = 1, int pageSize = 50) =>
        new CarRepository(db).SearchCatalogAsync(query, page, pageSize);

    private static IEnumerable<int> Ids(PagedResult<Car> result) => result.Items.Select(c => c.Id);

    // =========================================================================
    //  Статус і текстовий пошук
    // =========================================================================

    [Fact]
    public async Task SearchCatalog_WithoutFilters_ReturnsOnlyAvailableCars()
    {
        await using var db = await SeedAsync(
            MakeCar(1, "Toyota", "Camry"),
            MakeCar(2, "Honda", "Civic", status: CarStatus.Rented),
            MakeCar(3, "BMW", "X5", status: CarStatus.UnderMaintenance));

        var result = await Search(db, new CarCatalogQuery());

        Ids(result).Should().Equal(1);
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchCatalog_SearchIsCaseInsensitiveAndEveryWordMustMatch()
    {
        await using var db = await SeedAsync(
            MakeCar(1, "Toyota", "Camry"),
            MakeCar(2, "Toyota", "RAV4", categoryId: 2),
            MakeCar(3, "Kia", "Sportage", categoryId: 2));

        var result = await Search(db, new CarCatalogQuery { Search = "  toyota   rav4 " });

        Ids(result).Should().Equal(2);
    }

    [Fact]
    public async Task SearchCatalog_SearchLooksInDescriptionAndLocation()
    {
        await using var db = await SeedAsync(
            MakeCar(1, "Toyota", "Camry", description: "Комфортний сімейний седан"),
            MakeCar(2, "Kia", "Ceed", location: "Львів, пр. Свободи, 5"),
            MakeCar(3, "BMW", "X5"));

        (await Search(db, new CarCatalogQuery { Search = "сімейний" })).Items.Should().ContainSingle(c => c.Id == 1);
        (await Search(db, new CarCatalogQuery { Search = "львів" })).Items.Should().ContainSingle(c => c.Id == 2);
    }

    // =========================================================================
    //  Фільтри
    // =========================================================================

    [Fact]
    public async Task SearchCatalog_CityMatchesTheTextBeforeTheFirstComma()
    {
        await using var db = await SeedAsync(
            MakeCar(1, "Toyota", "Camry", location: "Київ, вул. Хрещатик, 10"),
            MakeCar(2, "Kia", "Ceed", location: "Київ"),
            MakeCar(3, "BMW", "X5", location: "Київська обл., Бровари"),
            MakeCar(4, "Skoda", "Fabia", location: "Львів, пр. Свободи, 5"));

        var result = await Search(db, new CarCatalogQuery { City = " Київ " });

        Ids(result).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task SearchCatalog_CombinesCategoryTransmissionFuelSeatsAndPrice()
    {
        await using var db = await SeedAsync(
            MakeCar(1, "Kia", "Sportage", categoryId: 2, transmission: Transmission.Automatic, fuel: FuelType.Diesel, seats: 7, price: 1700),
            MakeCar(2, "Renault", "Duster", categoryId: 2, transmission: Transmission.Manual, fuel: FuelType.Diesel, seats: 7, price: 1000),
            MakeCar(3, "Volvo", "XC90", categoryId: 2, transmission: Transmission.Automatic, fuel: FuelType.Diesel, seats: 7, price: 3200),
            MakeCar(4, "Toyota", "RAV4", categoryId: 2, transmission: Transmission.Automatic, fuel: FuelType.Hybrid, seats: 7, price: 2000),
            MakeCar(5, "Kia", "Sorento", categoryId: 2, transmission: Transmission.Automatic, fuel: FuelType.Diesel, seats: 5, price: 1800),
            MakeCar(6, "Skoda", "Superb", categoryId: 1, transmission: Transmission.Automatic, fuel: FuelType.Diesel, seats: 7, price: 1500));

        var result = await Search(db, new CarCatalogQuery
        {
            CategoryId     = 2,
            Transmission   = Transmission.Automatic,
            FuelType       = FuelType.Diesel,
            MinSeats       = 7,
            MaxPricePerDay = 2000
        });

        Ids(result).Should().Equal(1);
    }

    [Fact]
    public async Task SearchCatalog_WithDates_ExcludesCarsWithActiveOverlappingRentals()
    {
        await using var db = await SeedAsync(
            MakeCar(1, "Toyota", "Camry"),   // підтверджена оренда перетинається
            MakeCar(2, "Honda", "Civic"),    // заявка (Pending) перетинається
            MakeCar(3, "BMW", "X5"),         // перетинається лише скасована оренда
            MakeCar(4, "Kia", "Ceed"),       // повернення в день отримання — не перетин
            MakeCar(5, "Skoda", "Fabia"));   // без оренд

        var pickup = Today.AddDays(10);
        var ret    = Today.AddDays(13);
        db.Rentals.AddRange(
            new Rental { Id = 1, UserId = "u1", CarId = 1, PickupDate = Today.AddDays(12), ReturnDate = Today.AddDays(15), Status = RentalStatus.Confirmed },
            new Rental { Id = 2, UserId = "u1", CarId = 2, PickupDate = Today.AddDays(8),  ReturnDate = Today.AddDays(11), Status = RentalStatus.Pending },
            new Rental { Id = 3, UserId = "u1", CarId = 3, PickupDate = Today.AddDays(10), ReturnDate = Today.AddDays(13), Status = RentalStatus.Cancelled },
            new Rental { Id = 4, UserId = "u1", CarId = 4, PickupDate = Today.AddDays(7),  ReturnDate = Today.AddDays(10), Status = RentalStatus.Confirmed });
        await db.SaveChangesAsync();

        var result = await Search(db, new CarCatalogQuery { PickupDate = pickup, ReturnDate = ret });

        Ids(result).Should().BeEquivalentTo(new[] { 3, 4, 5 });
    }

    [Fact]
    public async Task SearchCatalog_WithOnlyOneDate_DoesNotFilterByAvailability()
    {
        await using var db = await SeedAsync(MakeCar(1, "Toyota", "Camry"));
        db.Rentals.Add(new Rental { Id = 1, UserId = "u1", CarId = 1, PickupDate = Today.AddDays(1), ReturnDate = Today.AddDays(5), Status = RentalStatus.Confirmed });
        await db.SaveChangesAsync();

        var result = await Search(db, new CarCatalogQuery { PickupDate = Today.AddDays(2) });

        Ids(result).Should().Equal(1);
    }

    // =========================================================================
    //  Сортування і сторінки
    // =========================================================================

    [Theory]
    [InlineData(CarSort.PriceAsc,  new[] { 2, 3, 1 })]
    [InlineData(CarSort.PriceDesc, new[] { 1, 3, 2 })]
    [InlineData(CarSort.Newest,    new[] { 3, 1, 2 })]
    [InlineData(CarSort.Default,   new[] { 2, 1, 3 })] // Sedan: Honda, Toyota; потім Wagon
    public async Task SearchCatalog_SortsAsRequested(CarSort sort, int[] expectedIds)
    {
        await using var db = await SeedAsync(
            MakeCar(1, "Toyota", "Camry", categoryId: 1, price: 1500, year: 2022),
            MakeCar(2, "Honda", "Civic", categoryId: 1, price: 900, year: 2020),
            MakeCar(3, "BMW", "X5", categoryId: 2, price: 1200, year: 2024));

        var result = await Search(db, new CarCatalogQuery { Sort = sort });

        Ids(result).Should().Equal(expectedIds);
    }

    [Fact]
    public async Task SearchCatalog_ReturnsRequestedPageAndTotalCount()
    {
        await using var db = await SeedAsync(Enumerable.Range(1, 7)
            .Select(i => MakeCar(i, "Brand", $"Model{i}", price: 100 * i)).ToArray());

        var result = await Search(db, new CarCatalogQuery { Sort = CarSort.PriceAsc }, page: 2, pageSize: 3);

        Ids(result).Should().Equal(4, 5, 6);
        result.TotalCount.Should().Be(7);
        result.Page.Should().Be(2);
    }

    [Theory]
    [InlineData(99, 3)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public async Task SearchCatalog_PageOutOfRange_IsClampedToExistingPages(int requested, int expected)
    {
        await using var db = await SeedAsync(Enumerable.Range(1, 7)
            .Select(i => MakeCar(i, "Brand", $"Model{i}", price: 100 * i)).ToArray());

        var result = await Search(db, new CarCatalogQuery { Sort = CarSort.PriceAsc }, page: requested, pageSize: 3);

        result.Page.Should().Be(expected);
        result.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SearchCatalog_WhenNothingMatches_ReturnsEmptyFirstPage()
    {
        await using var db = await SeedAsync(MakeCar(1, "Toyota", "Camry"));

        var result = await Search(db, new CarCatalogQuery { Search = "ferrari" }, page: 4, pageSize: 6);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.Page.Should().Be(1);
    }

    // =========================================================================
    //  GetCitiesAsync
    // =========================================================================

    [Fact]
    public async Task GetCities_ReturnsDistinctCitiesOfAvailableCarsAlphabetically()
    {
        await using var db = await SeedAsync(
            MakeCar(1, "A", "1", location: "Львів, пр. Свободи, 5"),
            MakeCar(2, "B", "2", location: "Київ, вул. Хрещатик, 10"),
            MakeCar(3, "C", "3", location: "Київ, вул. Велика Васильківська, 25"),
            MakeCar(4, "D", "4", location: "Одеса"),
            MakeCar(5, "E", "5", location: "Харків, вул. Сумська, 20", status: CarStatus.UnderMaintenance));

        var cities = await new CarRepository(db).GetCitiesAsync();

        cities.Should().Equal("Київ", "Львів", "Одеса");
    }
}
