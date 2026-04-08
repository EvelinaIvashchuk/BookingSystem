using BookingSystem.Enums;
using BookingSystem.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Car>      Cars       { get; set; } = null!;
    public DbSet<Rental>   Rentals    { get; set; } = null!;
    public DbSet<Payment>  Payments   { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── Category ──────────────────────────────────────────────────────────
        builder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(100);
            e.Property(c => c.Description).HasMaxLength(300);
            e.HasIndex(c => c.Name).IsUnique();
        });

        // ── Car ───────────────────────────────────────────────────────────────
        builder.Entity<Car>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Brand).IsRequired().HasMaxLength(50);
            e.Property(c => c.Model).IsRequired().HasMaxLength(50);
            e.Property(c => c.LicensePlate).IsRequired().HasMaxLength(20);
            e.Property(c => c.Description).HasMaxLength(500);
            e.Property(c => c.Location).HasMaxLength(200);
            e.Property(c => c.ImageUrl).HasMaxLength(500);
            e.Property(c => c.PricePerDay).HasColumnType("decimal(18,2)");

            e.Property(c => c.Status)
             .HasConversion<string>()
             .HasMaxLength(30);

            e.Property(c => c.FuelType)
             .HasConversion<string>()
             .HasMaxLength(20);

            e.Property(c => c.Transmission)
             .HasConversion<string>()
             .HasMaxLength(20);

            e.HasIndex(c => c.LicensePlate).IsUnique();

            e.HasOne(c => c.Category)
             .WithMany(cat => cat.Cars)
             .HasForeignKey(c => c.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Rental ────────────────────────────────────────────────────────────
        builder.Entity<Rental>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Notes).HasMaxLength(500);
            e.Property(r => r.AdminNote).HasMaxLength(500);
            e.Property(r => r.TotalPrice).HasColumnType("decimal(18,2)");

            e.Property(r => r.Status)
             .HasConversion<string>()
             .HasMaxLength(20);

            e.Property(r => r.PickupDate).IsRequired();
            e.Property(r => r.ReturnDate).IsRequired();

            e.HasOne(r => r.User)
             .WithMany(u => u.Rentals)
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.Car)
             .WithMany(c => c.Rentals)
             .HasForeignKey(r => r.CarId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(r => new { r.CarId, r.PickupDate, r.ReturnDate })
             .HasDatabaseName("IX_Rental_Car_DateRange");
        });

        // ── Payment ───────────────────────────────────────────────────────────
        builder.Entity<Payment>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            e.Property(p => p.TransactionReference).HasMaxLength(100);

            e.Property(p => p.Status)
             .HasConversion<string>()
             .HasMaxLength(20);

            e.HasOne(p => p.Rental)
             .WithOne()
             .HasForeignKey<Payment>(p => p.RentalId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Seed Data ─────────────────────────────────────────────────────────
        SeedData(builder);
    }

    private static void SeedData(ModelBuilder builder)
    {
        builder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Седан",      Description = "Комфортні седани для міських та міжміських поїздок" },
            new Category { Id = 2, Name = "SUV",        Description = "Позашляховики та кросовери для будь-яких доріг" },
            new Category { Id = 3, Name = "Хетчбек",    Description = "Компактні автомобілі для міста" },
            new Category { Id = 4, Name = "Мінівен",    Description = "Просторі автомобілі для сімейних поїздок" },
            new Category { Id = 5, Name = "Купе",       Description = "Спортивні автомобілі для задоволення від їзди" }
        );

        builder.Entity<Car>().HasData(
            new Car
            {
                Id = 1, Brand = "Toyota", Model = "Camry", Year = 2023,
                LicensePlate = "AA1234BB", FuelType = FuelType.Petrol,
                Transmission = Transmission.Automatic, Seats = 5,
                PricePerDay = 1200m, Location = "Київ, вул. Хрещатик 10",
                CategoryId = 1, Status = CarStatus.Available,
                Description = "Комфортний седан з кліма-контролем та круїз-контролем."
            },
            new Car
            {
                Id = 2, Brand = "Honda", Model = "CR-V", Year = 2024,
                LicensePlate = "AA5678CC", FuelType = FuelType.Hybrid,
                Transmission = Transmission.Automatic, Seats = 5,
                PricePerDay = 1800m, Location = "Київ, вул. Велика Васильківська 25",
                CategoryId = 2, Status = CarStatus.Available,
                Description = "Просторий гібридний кросовер з повним приводом."
            },
            new Car
            {
                Id = 3, Brand = "Volkswagen", Model = "Golf", Year = 2023,
                LicensePlate = "AA9012DD", FuelType = FuelType.Diesel,
                Transmission = Transmission.Manual, Seats = 5,
                PricePerDay = 900m, Location = "Львів, пр. Свободи 5",
                CategoryId = 3, Status = CarStatus.Available,
                Description = "Економний дизельний хетчбек. Ідеальний для міста."
            },
            new Car
            {
                Id = 4, Brand = "Toyota", Model = "Sienna", Year = 2022,
                LicensePlate = "BB3456EE", FuelType = FuelType.Hybrid,
                Transmission = Transmission.Automatic, Seats = 7,
                PricePerDay = 2200m, Location = "Київ, вул. Хрещатик 10",
                CategoryId = 4, Status = CarStatus.Available,
                Description = "Великий мінівен для сімейних подорожей. 7 місць."
            },
            new Car
            {
                Id = 5, Brand = "BMW", Model = "4 Series", Year = 2024,
                LicensePlate = "CC7890FF", FuelType = FuelType.Petrol,
                Transmission = Transmission.Automatic, Seats = 4,
                PricePerDay = 3500m, Location = "Одеса, Дерибасівська 15",
                CategoryId = 5, Status = CarStatus.Available,
                Description = "Спортивне купе з потужним двигуном та преміум-інтер'єром."
            },
            new Car
            {
                Id = 6, Brand = "Nissan", Model = "Leaf", Year = 2023,
                LicensePlate = "DD1122GG", FuelType = FuelType.Electric,
                Transmission = Transmission.Automatic, Seats = 5,
                PricePerDay = 800m, Location = "Харків, вул. Сумська 20",
                CategoryId = 3, Status = CarStatus.UnderMaintenance,
                Description = "Електромобіль. Наразі на технічному обслуговуванні."
            }
        );
    }
}
