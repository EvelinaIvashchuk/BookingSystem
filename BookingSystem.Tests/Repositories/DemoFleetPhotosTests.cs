using BookingSystem.Data;
using BookingSystem.Models;
using BookingSystem.Tests.Helpers;
using FluentAssertions;

namespace BookingSystem.Tests.Repositories;

public class DemoFleetPhotosTests
{
    private static readonly string[] InitialCarPlates = ["AA1234BB", "AA5678CC", "AA9012DD", "BB3456EE", "CC7890FF", "DD1122GG"];

    [Fact]
    public void EveryDemoCar_HasAPhotoWithAttribution()
    {
        var plates = InitialCarPlates.Concat(DemoFleet.Create().Select(c => c.LicensePlate)).ToList();

        plates.Should().HaveCount(50);
        DemoFleetPhotos.ByPlate.Keys.Should().BeEquivalentTo(plates);
        DemoFleetPhotos.ByPlate.Values.Should().OnlyContain(p =>
            p.Url.StartsWith("https://") && p.Url.Length <= 500
            && p.SourcePage.StartsWith("https://commons.wikimedia.org/wiki/File:")
            && !string.IsNullOrWhiteSpace(p.Author)
            && p.LicenseUrl.StartsWith("https://creativecommons.org/"));
    }

    [Fact]
    public void Photos_UseOnlyLicencesThatAllowReuseWithAttribution()
    {
        DemoFleetPhotos.ByPlate.Values.Select(p => p.License).Distinct()
            .Should().OnlyContain(l => l.StartsWith("CC BY ") || l.StartsWith("CC BY-SA "));
    }

    [Fact]
    public void NewDemoCars_GetTheirPhoto()
    {
        DemoFleet.Create().Should().OnlyContain(c => c.ImageUrl == DemoFleetPhotos.ByPlate[c.LicensePlate].Url);
    }

    [Fact]
    public async Task FillMissing_SetsPhotoOnlyWhereThereIsNoImage()
    {
        await using var db = TestDbContextFactory.Create();
        db.Categories.Add(new Category { Id = 1, Name = "Седан" });
        db.Cars.AddRange(
            NewCar(1, "AA1234BB", imageUrl: null),
            NewCar(2, "AA5678CC", imageUrl: "cars/uploaded.jpg"),
            NewCar(3, "XX0000XX", imageUrl: null));
        await db.SaveChangesAsync();

        var updated = await DemoFleetPhotos.FillMissingAsync(db);

        updated.Should().Be(1);
        db.Cars.Single(c => c.Id == 1).ImageUrl.Should().Be(DemoFleetPhotos.ByPlate["AA1234BB"].Url);
        db.Cars.Single(c => c.Id == 2).ImageUrl.Should().Be("cars/uploaded.jpg");
        db.Cars.Single(c => c.Id == 3).ImageUrl.Should().BeNull();
    }

    private static Car NewCar(int id, string plate, string? imageUrl) => new()
    {
        Id = id, Brand = "Test", Model = "Car", Year = 2023, LicensePlate = plate, CategoryId = 1,
        Seats = 5, PricePerDay = 1000, Location = "Київ", ImageUrl = imageUrl
    };
}
