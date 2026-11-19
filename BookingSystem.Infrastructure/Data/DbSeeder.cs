using BookingSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data;

public static class DbSeeder
{
    public const string AdminRole = "Admin";
    public const string UserRole  = "User";

    public static async Task SeedRolesAndAdminAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<ApplicationDbContext>>();

        try
        {
            var db          = services.GetRequiredService<ApplicationDbContext>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var config      = services.GetRequiredService<IConfiguration>();

            try
            {
                var pending = await db.Database.GetPendingMigrationsAsync();
                if (pending.Any())
                    await db.Database.MigrateAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "MigrateAsync failed — falling back to EnsureCreatedAsync.");
                await db.Database.EnsureCreatedAsync();
            }

            foreach (var role in new[] { AdminRole, UserRole })
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            var adminEmail    = config["Seed:AdminEmail"]    ?? "admin@carrental.local";
            var adminPassword = config["Seed:AdminPassword"] ?? "Admin@12345";

            if (await userManager.FindByEmailAsync(adminEmail) is null)
            {
                var admin = new ApplicationUser
                {
                    UserName       = adminEmail,
                    Email          = adminEmail,
                    FirstName      = "System",
                    LastName       = "Admin",
                    IsActive       = true,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(admin, adminPassword);

                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, AdminRole);
                    logger.LogInformation("Admin account seeded: {Email}", adminEmail);
                }
                else
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    logger.LogError("Failed to seed admin account: {Errors}", errors);
                }
            }

            // Demo fleet: adds only missing cars (by licence plate), never changes existing ones,
            // except that demo cars without an image get their Wikimedia Commons photo.
            if (!string.Equals(config["Seed:DemoFleet"], "false", StringComparison.OrdinalIgnoreCase))
            {
                var added = await DemoFleet.EnsureAsync(db, keepIds: false);
                if (added > 0)
                    logger.LogInformation("Demo fleet seeded: {Count} cars added", added);

                var photos = await DemoFleetPhotos.FillMissingAsync(db);
                if (photos > 0)
                    logger.LogInformation("Demo fleet photos set for {Count} cars", photos);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Database seeding failed. The application will start without seeded data. " +
                "Check the connection string and ensure MySQL is reachable.");
        }
    }
}
