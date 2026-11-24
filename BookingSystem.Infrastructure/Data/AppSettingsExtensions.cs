using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Data;

/// <summary>Reads and writes administrator-changed settings stored in the <c>AppSettings</c> table.</summary>
public static class AppSettingsExtensions
{
    public static Task<string?> GetSettingAsync(this ApplicationDbContext db, string key, CancellationToken ct = default) =>
        db.AppSettings.AsNoTracking().Where(s => s.Key == key).Select(s => (string?)s.Value).FirstOrDefaultAsync(ct);

    public static async Task SetSettingAsync(this ApplicationDbContext db, string key, string value, DateTime nowUtc, CancellationToken ct = default)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
            db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAtUtc = nowUtc });
        else
        {
            setting.Value = value;
            setting.UpdatedAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(ct);
    }
}
