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
    /// </summary>
    public static async Task SeedRolesAndAdminAsync(IServiceProvider services)
    {
        var db          = services.GetRequiredService<ApplicationDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var config      = services.GetRequiredService<IConfiguration>();
        var logger      = services.GetRequiredService<ILogger<ApplicationDbContext>>();

        try
        {
            // GetPendingMigrationsAsync() throws if no migrations assembly exists.
            // Check first so we can fall back to EnsureCreated for first-time setup.
            var pending = await db.Database.GetPendingMigrationsAsync();
            if (pending.Any())
                await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            // No migrations have been created yet (fresh project).
            // EnsureCreatedAsync builds the schema directly from the model.
            logger.LogWarning(ex,
                "MigrateAsync failed — no migrations found. Falling back to EnsureCreatedAsync.");
            await db.Database.EnsureCreatedAsync();
        }

        // Seed roles
        foreach (var role in new[] { AdminRole, UserRole })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Seed admin account — override credentials via appsettings or env vars
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
}
