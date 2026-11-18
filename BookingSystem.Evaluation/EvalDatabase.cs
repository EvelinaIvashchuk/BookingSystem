using BookingSystem.Data;
using BookingSystem.Enums;
using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookingSystem.Evaluation;

/// <summary>
/// Separate database <c>bookingsystem_eval</c> with a deterministic fleet: the 6 cars seeded by the
/// initial migration plus the shared <see cref="DemoFleet"/> (50 in total, ids 1–50) and a few
/// existing rentals, so that scenarios also test availability. Seeding is idempotent.
/// </summary>
public sealed class EvalDatabase(ApplicationDbContext db, ILogger<EvalDatabase> logger)
{
    public const string UserId = "eval-user";
    public const string OtherUserId = "eval-other";

    public async Task PrepareAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.Users.AnyAsync(u => u.Id == UserId, ct))
        {
            db.Users.AddRange(User(UserId, "eval.user@carrental.local", "Олена"), User(OtherUserId, "eval.other@carrental.local", "Іван"));
            await db.SaveChangesAsync(ct);
        }

        // Cars 1–6 come from the migration seed, 7–50 from the shared demo fleet with fixed ids.
        await DemoFleet.EnsureAsync(db, keepIds: true, ct);

        if (!await db.Rentals.AnyAsync(ct))
        {
            db.Rentals.AddRange(
                Rental(OtherUserId, 1, "2026-12-18", "2026-12-23", RentalStatus.Confirmed),   // Camry around Christmas
                Rental(OtherUserId, 14, "2026-12-01", "2026-12-10", RentalStatus.Pending),    // Sportage early December
                Rental(OtherUserId, 17, "2026-12-20", "2026-12-27", RentalStatus.Confirmed),  // Ceed
                Rental(OtherUserId, 7, "2026-11-27", "2026-11-30", RentalStatus.Confirmed),   // Octavia
                Rental(OtherUserId, 13, "2026-12-24", "2027-01-03", RentalStatus.Confirmed),  // RAV4 New Year
                Rental(OtherUserId, 3, "2026-12-01", "2026-12-04", RentalStatus.Cancelled));  // cancelled: does not block
            await db.SaveChangesAsync(ct);
        }

        // Drafts and chat sessions from previous runs are not needed and must not leak between runs.
        await db.PendingActions.ExecuteDeleteAsync(ct);
        await db.ChatMessages.ExecuteDeleteAsync(ct);
        await db.ChatSessions.ExecuteDeleteAsync(ct);

        logger.LogInformation("Eval database ready: {Cars} cars, {Rentals} rentals",
            await db.Cars.CountAsync(ct), await db.Rentals.CountAsync(ct));
    }

    private static ApplicationUser User(string id, string email, string firstName) => new()
    {
        Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email,
        NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, FirstName = firstName, LastName = "Тестовий",
        SecurityStamp = Guid.NewGuid().ToString()
    };

    private static Rental Rental(string userId, int carId, string pickup, string ret, RentalStatus status) => new()
    {
        UserId = userId, CarId = carId, PickupDate = DateTime.Parse(pickup), ReturnDate = DateTime.Parse(ret),
        Status = status, TotalPrice = 0, CreatedAt = new DateTime(2026, 11, 1)
    };

}
