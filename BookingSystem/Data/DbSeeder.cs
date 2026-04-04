using BookingSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data;

public static class DbSeeder
{
    public const string AdminRole = "Admin";
    public const string UserRole  = "User";

    /// <summary>
    /// Applies pending EF migrations (or creates the schema if none exist),
    /// seeds roles, and creates the default admin account if absent.
    /// Wraps everything in a top-level try/catch so a transient DB failure
    /// does not crash the entire application on startup.
    /// </summary>
    public static async Task SeedRolesAndAdminAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<ApplicationDbContext>>();

        try
        {
            var db          = services.GetRequiredService<ApplicationDbContext>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var config      = services.GetRequiredService<IConfiguration>();

            // ── Schema ───────────────────────────────────────────────────────────
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

            // ── Roles ────────────────────────────────────────────────────────────
            foreach (var role in new[] { AdminRole, UserRole })
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            // ── Admin account ────────────────────────────────────────────────────
            var adminEmail    = config["Seed:AdminEmail"]    ?? "admin@bookingsystem.local";
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
        }
        catch (Exception ex)
        {
            // Log and continue — the app should still start even if seeding fails.
            // On the next request EF will surface a proper connection error.
            logger.LogError(ex,
                "Database seeding failed. The application will start without seeded data. " +
                "Check the connection string and ensure MySQL is reachable.");
        }
    }
}
