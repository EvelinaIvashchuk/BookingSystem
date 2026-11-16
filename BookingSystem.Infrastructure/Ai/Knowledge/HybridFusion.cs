namespace BookingSystem.Ai.Knowledge;

/// <summary>
/// Rank fusion of the vector and full-text candidate lists.
/// <para>Classic RRF: score(d) = 1/(k + r_vec(d)) + 1/(k + r_ft(d)).</para>
/// <para>Adaptive (proposed): score(d) = (w_vec/(k + r_vec(d)) + w_ft/(k + r_ft(d))) · (1 + β·[cat(d) = cat(q)]).</para>
/// Ranks are 1-based; a document missing from a list contributes nothing from that list.
/// The category is a soft boost, not a filter, so a wrong prediction cannot remove a relevant document.
/// </summary>
public static class HybridFusion
{
    public sealed record FusedItem(int Id, double Score, int? VectorRank, int? FullTextRank);

    public static IReadOnlyList<FusedItem> Fuse(
        IReadOnlyList<int> vectorRanking,
        IReadOnlyList<int> fullTextRanking,
        int k,
        double vectorWeight = 1,
        double fullTextWeight = 1,
        Func<int, double>? boost = null)
    {
        var vectorRanks = Ranks(vectorRanking);
        var fullTextRanks = Ranks(fullTextRanking);

        return vectorRanks.Keys.Union(fullTextRanks.Keys)
            .Select(id =>
            {
                int? rv = vectorRanks.TryGetValue(id, out var v) ? v : null;
                int? rf = fullTextRanks.TryGetValue(id, out var f) ? f : null;

                var score = (rv is { } a ? vectorWeight / (k + a) : 0)
                          + (rf is { } b ? fullTextWeight / (k + b) : 0);
                score *= boost?.Invoke(id) ?? 1;

                return new FusedItem(id, score, rv, rf);
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Math.Min(x.VectorRank ?? int.MaxValue, x.FullTextRank ?? int.MaxValue))
            .ThenBy(x => x.Id)
            .ToList();
    }

    private static Dictionary<int, int> Ranks(IReadOnlyList<int> ranking)
    {
        var ranks = new Dictionary<int, int>();
        for (var i = 0; i < ranking.Count; i++)
            ranks.TryAdd(ranking[i], i + 1);
        return ranks;
    }
}
