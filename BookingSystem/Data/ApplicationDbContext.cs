using BookingSystem.Enums;
using BookingSystem.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    // Standard auto-properties — expression-bodied DbSet props (=> Set<T>())
    // can confuse EF migration tooling in some SDK versions.
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Resource>  Resources  { get; set; } = null!;
    public DbSet<Booking>   Bookings   { get; set; } = null!;
    public DbSet<Payment>   Payments   { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Required — configures Identity tables

        // ── Category ──────────────────────────────────────────────────────────
        builder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(100);
            e.Property(c => c.Description).HasMaxLength(300);
            e.HasIndex(c => c.Name).IsUnique();
        });

        // ── Resource ──────────────────────────────────────────────────────────
        builder.Entity<Resource>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).IsRequired().HasMaxLength(100);
            e.Property(r => r.Description).HasMaxLength(500);
            e.Property(r => r.Location).IsRequired().HasMaxLength(200);
            e.Property(r => r.ImageUrl).HasMaxLength(500);

            e.Property(r => r.Status)
             .HasConversion<string>()
             .HasMaxLength(30);

            // Restrict: cannot delete a category that still has resources
            e.HasOne(r => r.Category)
             .WithMany(c => c.Resources)
             .HasForeignKey(r => r.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Booking ───────────────────────────────────────────────────────────
        builder.Entity<Booking>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Purpose).HasMaxLength(500);
            e.Property(b => b.AdminNote).HasMaxLength(500);

            e.Property(b => b.Status)
             .HasConversion<string>()
             .HasMaxLength(20);

            // StartTime/EndTime are DateTime? in the model but stored as non-nullable
            // columns — the DB values will always be set before saving.
            e.Property(b => b.StartTime).IsRequired();
            e.Property(b => b.EndTime).IsRequired();

            // Restrict: preserve booking history if user is deactivated
            e.HasOne(b => b.User)
             .WithMany(u => u.Bookings)
             .HasForeignKey(b => b.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Restrict: cannot delete a resource with existing bookings
            e.HasOne(b => b.Resource)
             .WithMany(r => r.Bookings)
             .HasForeignKey(b => b.ResourceId)
             .OnDelete(DeleteBehavior.Restrict);

            // Composite index speeds up time-overlap conflict queries
            e.HasIndex(b => new { b.ResourceId, b.StartTime, b.EndTime })
             .HasDatabaseName("IX_Booking_Resource_TimeRange");
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

            // One-to-one: cascade delete removes payment when booking is deleted
            e.HasOne(p => p.Booking)
             .WithOne()
             .HasForeignKey<Payment>(p => p.BookingId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Seed Data ─────────────────────────────────────────────────────────
        SeedData(builder);
    }

    private static void SeedData(ModelBuilder builder)
    {
        builder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Meeting Rooms",     Description = "Rooms for meetings and conferences" },
            new Category { Id = 2, Name = "Sports Facilities", Description = "Gyms, courts, and sports halls" },
            new Category { Id = 3, Name = "Workspaces",        Description = "Hot desks and private offices" },
            new Category { Id = 4, Name = "Equipment",         Description = "AV, cameras, and other loan equipment" }
        );

        builder.Entity<Resource>().HasData(
            new Resource
            {
                Id = 1, Name = "Conference Room A", Location = "Floor 2, East Wing",
                Capacity = 10, CategoryId = 1, Status = ResourceStatus.Available,
                Description = "Large conference room with projector and whiteboard."
            },
            new Resource
            {
                Id = 2, Name = "Board Room", Location = "Floor 4",
                Capacity = 20, CategoryId = 1, Status = ResourceStatus.Available,
                Description = "Executive board room with video conferencing."
            },
            new Resource
            {
                Id = 3, Name = "Squash Court 1", Location = "Sports Centre, Ground Floor",
                Capacity = 2, CategoryId = 2, Status = ResourceStatus.Available,
                Description = "Full-size squash court. Rackets available on request."
            },
            new Resource
            {
                Id = 4, Name = "Hot Desk Zone A", Location = "Floor 1, Open Plan",
                Capacity = 1, CategoryId = 3, Status = ResourceStatus.Available,
                Description = "Quiet hot desk with power and USB-C docking station."
            },
            new Resource
            {
                Id = 5, Name = "4K Projector Kit", Location = "IT Store, Floor 1",
                Capacity = 1, CategoryId = 4, Status = ResourceStatus.Available,
                Description = "Portable 4K projector with HDMI and carry case."
            },
            new Resource
            {
                Id = 6, Name = "Training Room B", Location = "Floor 3, West Wing",
                Capacity = 30, CategoryId = 1, Status = ResourceStatus.UnderMaintenance,
                Description = "Large training room. Currently being refurbished."
            }
        );
    }
}
