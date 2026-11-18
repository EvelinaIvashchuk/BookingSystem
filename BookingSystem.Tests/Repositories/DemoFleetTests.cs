using BookingSystem.Data;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Tests.Repositories;

public class DemoFleetTests
{
    private static async Task<ApplicationDbContext> DbWithCategoriesAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Categories.AddRange(
            new Category { Id = 1, Name = "Седан" }, new Category { Id = 2, Name = "SUV" },
            new Category { Id = 3, Name = "Хетчбек" }, new Category { Id = 4, Name = "Мінівен" },
            new Category { Id = 5, Name = "Купе" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public void Fleet_Has44CarsWithUniquePlatesAndIds7To50()
    {
        var cars = DemoFleet.Create();

        cars.Should().HaveCount(44);
        cars.Select(c => c.Id).Should().Equal(Enumerable.Range(7, 44));
        cars.Select(c => c.LicensePlate).Should().OnlyHaveUniqueItems();
        cars.Should().OnlyContain(c => c.LicensePlate.Length <= 20 && c.PricePerDay > 0 && c.Seats >= 1);
    }

    [Fact]
    public void Fleet_CoversCategoriesCitiesTransmissionsAndFuels()
    {
        var cars = DemoFleet.Create();

        cars.Select(c => c.CategoryId).Distinct().Should().BeEquivalentTo([1, 2, 3, 4, 5]);
        cars.Select(c => c.Location.Split(',')[0]).Distinct().Should()
            .BeEquivalentTo(["Київ", "Львів", "Одеса", "Харків", "Дніпро", "Чернівці"]);
        cars.Select(c => c.Transmission).Distinct().Should().HaveCount(2);
        cars.Select(c => c.FuelType).Distinct().Should().BeEquivalentTo(Enum.GetValues<FuelType>());
        cars.Should().Contain(c => c.Status == CarStatus.UnderMaintenance);
    }

    [Fact]
    public async Task EnsureAsync_AddsMissingCarsOnceAndKeepsExistingOnes()
    {
        await using var db = await DbWithCategoriesAsync();
        var adminCar = DemoFleet.Create()[0];
        adminCar.Id = 0;
        adminCar.PricePerDay = 999;                 // an admin already added and edited this car
        db.Cars.Add(adminCar);
        await db.SaveChangesAsync();

        var first = await DemoFleet.EnsureAsync(db, keepIds: false);
        var second = await DemoFleet.EnsureAsync(db, keepIds: false);

        first.Should().Be(43);
        second.Should().Be(0);
        (await db.Cars.CountAsync()).Should().Be(44);
        (await db.Cars.SingleAsync(c => c.LicensePlate == adminCar.LicensePlate)).PricePerDay.Should().Be(999);
    }

    [Fact]
    public async Task EnsureAsync_WithoutCategories_DoesNothing()
    {
        await using var db = TestDbContextFactory.Create();

        (await DemoFleet.EnsureAsync(db, keepIds: true)).Should().Be(0);
    }
}
