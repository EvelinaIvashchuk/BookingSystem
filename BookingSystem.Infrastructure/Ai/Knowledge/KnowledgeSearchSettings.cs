using BookingSystem.Data;
using Microsoft.Extensions.Options;

namespace BookingSystem.Ai.Knowledge;

/// <summary>Stores the administrator's knowledge-base search mode in the <c>AppSettings</c> table (key <see cref="SettingKey"/>).</summary>
public sealed class KnowledgeSearchSettings(ApplicationDbContext db, IOptions<AiOptions> options, TimeProvider clock) : IKnowledgeSearchSettings
{
    public const string SettingKey = "Ai:SearchMode";

    public IReadOnlyList<KnowledgeSearchMode> GetAvailableModes() => Enum.GetValues<KnowledgeSearchMode>();

    public async Task<KnowledgeSearchMode> GetActiveModeAsync(CancellationToken cancellationToken = default)
    {
        var chosen = await db.GetSettingAsync(SettingKey, cancellationToken);
        return Enum.TryParse<KnowledgeSearchMode>(chosen, ignoreCase: false, out var mode) && Enum.IsDefined(mode)
            ? mode
            : options.Value.Search.Mode;
    }

    public async Task SetActiveModeAsync(KnowledgeSearchMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentException($"Search mode '{mode}' is not supported.", nameof(mode));

        await db.SetSettingAsync(SettingKey, mode.ToString(), clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
}
