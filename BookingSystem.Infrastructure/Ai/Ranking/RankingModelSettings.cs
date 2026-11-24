using BookingSystem.Data;
using Microsoft.Extensions.Options;

namespace BookingSystem.Ai.Ranking;

/// <summary>
/// Stores the administrator's car ranking model choice in the <c>AppSettings</c> table (key <see cref="SettingKey"/>).
/// Only models whose files exist can be chosen.
/// </summary>
public sealed class RankingModelSettings(ApplicationDbContext db, CarRanker ranker, IOptions<AiOptions> options, TimeProvider clock) : IRankingModelSettings
{
    public const string SettingKey = "Ai:RankingModel";

    public IReadOnlyList<RankingModelOption> GetAvailableModels() =>
        [.. ranker.AvailableModels().Select(m => new RankingModelOption(m.Id, m.TestNdcg5))];

    public async Task<string> GetActiveModelAsync(CancellationToken cancellationToken = default)
    {
        var available = GetAvailableModels();
        var chosen = await db.GetSettingAsync(SettingKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(chosen) && available.Any(m => m.Id == chosen))
            return chosen;

        var fallback = options.Value.CarRanking.DefaultModel;
        if (available.Any(m => m.Id == fallback) || available.Count == 0)
            return fallback;
        return available[0].Id;
    }

    public async Task SetActiveModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var id = modelId?.Trim();
        if (string.IsNullOrEmpty(id) || GetAvailableModels().All(m => m.Id != id))
            throw new ArgumentException($"Ranking model '{modelId}' is not available.", nameof(modelId));

        await db.SetSettingAsync(SettingKey, id, clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
}
