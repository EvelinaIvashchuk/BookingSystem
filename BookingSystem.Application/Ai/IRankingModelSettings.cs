namespace BookingSystem.Ai;

/// <summary>A car ranking model an administrator may choose; <paramref name="TestNdcg5"/> is its quality on the held-out test wishes (E5/E6).</summary>
/// <param name="Id"><c>lightgbm</c>, <c>mlp</c> or <c>two_tower</c>.</param>
public sealed record RankingModelOption(string Id, double? TestNdcg5);

/// <summary>
/// The model that orders cars by the client's wishes ("дві великі валізи, економне"), selectable by an administrator at runtime.
/// The choice is stored in the database and applies from the next search; until an administrator chooses,
/// <see cref="CarRankingOptions.DefaultModel"/> is used.
/// </summary>
public interface IRankingModelSettings
{
    /// <summary>Models whose files are present, in the order lightgbm, mlp, two_tower. Empty when ranking is switched off.</summary>
    IReadOnlyList<RankingModelOption> GetAvailableModels();

    /// <summary>The administrator's choice, or the default (first available model when the default is missing).</summary>
    Task<string> GetActiveModelAsync(CancellationToken cancellationToken = default);

    /// <exception cref="ArgumentException"><paramref name="modelId"/> is not in <see cref="GetAvailableModels"/>.</exception>
    Task SetActiveModelAsync(string modelId, CancellationToken cancellationToken = default);
}
