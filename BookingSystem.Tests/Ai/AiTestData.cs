using BookingSystem.Ai;
using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookingSystem.Tests.Ai;

/// <summary>
/// Small deterministic fleet for agent tool tests. "Today" is 2026-11-20; the typical
/// request is 2026-12-20 → 2026-12-25 (5 days).
/// </summary>
internal static class AiTestData
{
    public static readonly DateOnly Today = new(2026, 11, 20);

    /// <summary>2026-11-20 10:00 in Kyiv.</summary>
    public static readonly DateTimeOffset UtcNow = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    public const string Pickup = "2026-12-20";
    public const string Return = "2026-12-25";

    public const int CamryId = 1;       // Седан, Automatic, Petrol, 5 seats, 1200, Київ (back-to-back rental ends on pickup day)
    public const int CrvId = 2;         // SUV, Automatic, Hybrid, 5, 1800, Київ
    public const int GolfId = 3;        // Хетчбек, Manual, Diesel, 5, 900, Львів
    public const int SiennaId = 4;      // Мінівен, Automatic, Hybrid, 7, 2200, Київ
    public const int BmwId = 5;         // Купе, Automatic, Petrol, 4, 3500, Одеса
    public const int LeafId = 6;        // Хетчбек, Electric, under maintenance
    public const int OctaviaId = 7;     // Седан, Automatic, Diesel, 5, 1100, Київ — booked for the dates
    public const int CeedId = 8;        // Хетчбек, Automatic, Petrol, 5, 950, Київ — only a cancelled rental
    public const int DusterId = 9;      // SUV, Manual, Diesel, 5, 1000, Чернівці

    public static async Task SeedAsync(ApplicationDbContext db)
    {
        db.Users.AddRange(
            new ApplicationUser { Id = "user-1", UserName = "user1@test.local", Email = "user1@test.local", FirstName = "Олена", LastName = "Тест" },
            new ApplicationUser { Id = "other-user", UserName = "other@test.local", Email = "other@test.local", FirstName = "Інший", LastName = "Тест" });

        db.Categories.AddRange(
            new Category { Id = 1, Name = "Седан" },
            new Category { Id = 2, Name = "SUV" },
            new Category { Id = 3, Name = "Хетчбек" },
            new Category { Id = 4, Name = "Мінівен" },
            new Category { Id = 5, Name = "Купе" });

        db.Cars.AddRange(
            Car(CamryId, "Toyota", "Camry", 1, Transmission.Automatic, FuelType.Petrol, 5, 1200, "Київ, вул. Хрещатик 10"),
            Car(CrvId, "Honda", "CR-V", 2, Transmission.Automatic, FuelType.Hybrid, 5, 1800, "Київ, вул. Велика Васильківська 25"),
            Car(GolfId, "Volkswagen", "Golf", 3, Transmission.Manual, FuelType.Diesel, 5, 900, "Львів, пр. Свободи 5"),
            Car(SiennaId, "Toyota", "Sienna", 4, Transmission.Automatic, FuelType.Hybrid, 7, 2200, "Київ, вул. Хрещатик 10"),
            Car(BmwId, "BMW", "4 Series", 5, Transmission.Automatic, FuelType.Petrol, 4, 3500, "Одеса, Дерибасівська 15"),
            Car(LeafId, "Nissan", "Leaf", 3, Transmission.Automatic, FuelType.Electric, 5, 800, "Харків, вул. Сумська 20", CarStatus.UnderMaintenance),
            Car(OctaviaId, "Skoda", "Octavia", 1, Transmission.Automatic, FuelType.Diesel, 5, 1100, "Київ, пр. Перемоги 1"),
            Car(CeedId, "Kia", "Ceed", 3, Transmission.Automatic, FuelType.Petrol, 5, 950, "Київ, вул. Антоновича 3"),
            Car(DusterId, "Renault", "Duster", 2, Transmission.Manual, FuelType.Diesel, 5, 1000, "Чернівці, вул. Головна 1"));

        db.Rentals.AddRange(
            // Overlaps 20–25 Dec → Octavia is busy.
            Rental(100, "other-user", OctaviaId, "2026-12-22", "2026-12-27", RentalStatus.Confirmed),
            // Cancelled → does not block Ceed.
            Rental(101, "other-user", CeedId, "2026-12-20", "2026-12-25", RentalStatus.Cancelled),
            // Ends on the pickup day → back-to-back, Camry stays available.
            Rental(102, "user-1", CamryId, "2026-12-15", "2026-12-20", RentalStatus.Pending),
            Rental(103, "user-1", GolfId, "2026-11-01", "2026-11-05", RentalStatus.Confirmed));

        await db.SaveChangesAsync();
    }

    public static UnitOfWork CreateUnitOfWork(ApplicationDbContext db) =>
        new(db, new RentalRepository(db), new CarRepository(db),
            new ChatSessionRepository(db), new PendingActionRepository(db));

    public static AgentClock CreateClock(DateTimeOffset? utcNow = null, AiOptions? options = null) =>
        new(new FixedTimeProvider(utcNow ?? UtcNow), Options.Create(options ?? new AiOptions()));

    public static CarService CreateCarService(ApplicationDbContext db) =>
        new(new CarRepository(db), db, NullLogger<CarService>.Instance);

    private static Car Car(
        int id, string brand, string model, int categoryId, Transmission transmission, FuelType fuel,
        int seats, decimal price, string location, CarStatus status = CarStatus.Available) => new()
    {
        Id = id, Brand = brand, Model = model, Year = 2023, LicensePlate = $"AA{id:0000}BB",
        CategoryId = categoryId, Transmission = transmission, FuelType = fuel, Seats = seats,
        PricePerDay = price, Location = location, Status = status
    };

    private static Rental Rental(int id, string userId, int carId, string pickup, string ret, RentalStatus status) => new()
    {
        Id = id, UserId = userId, CarId = carId,
        PickupDate = DateTime.Parse(pickup), ReturnDate = DateTime.Parse(ret),
        Status = status, TotalPrice = 1000
    };
}
