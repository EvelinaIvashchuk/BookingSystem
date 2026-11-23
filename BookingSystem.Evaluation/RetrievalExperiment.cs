using System.Globalization;
using System.Text.Json;
using BookingSystem.Ai;
using BookingSystem.Ai.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingSystem.Evaluation;

/// <summary>
/// E3: Vector vs FullText vs HybridRrf vs HybridAdaptive.
/// <list type="number">
/// <item>For every query the vector and full-text candidate lists (top-20 each) are computed once.</item>
/// <item>Grid search of the adaptive parameters (a, b, c, n0, e, β) on the DEV split only, by MRR@10
///       (ties: Recall@3, then the combination closest to the defaults).</item>
/// <item>All methods are evaluated on the TEST split: Recall@3, Recall@5, MRR@10, also by query kind.</item>
/// <item>Paired bootstrap (10 000 resamples) of the MRR@10 difference adaptive − RRF on test.</item>
/// </list>
/// Fusion is recomputed locally with <see cref="HybridFusion"/> and <see cref="QueryFeatures"/> — the same
/// code the application uses — and checked against <see cref="IKnowledgeSearch"/> for the default parameters.
/// </summary>
public sealed class RetrievalExperiment(
    IServiceScopeFactory scopes,
    IOptions<AiOptions> aiOptions,
    EvalPaths paths,
    ILogger<RetrievalExperiment> logger)
{
    private const int TopN = 10;

    private sealed record Candidates(
        RetrievalQuery Query, IReadOnlyList<string> Vector, IReadOnlyList<string> FullText,
        QueryFeatures Features, IReadOnlyDictionary<string, string> Categories);

    internal sealed record Metrics(double Recall3, double Recall5, double Mrr10);

    public async Task RunAsync(string tuneOn, string evalOn, CancellationToken ct)
    {
        var queries = Datasets.LoadQrels(Path.Combine(paths.Datasets, "retrieval_qrels.json")).Queries;
        var search = aiOptions.Value.Search;
        var candidates = await CollectCandidatesAsync(queries, search.Candidates, ct);

        // 1) Grid search on the tuning split. v1 = the grid planned in advance; v2 = wider bounds
        //    (lower MinWeight, stronger length penalty) added after the dev breakdown showed that full text
        //    hurts descriptive questions. Both selections use the dev split only.
        // The hand-picked starting values (code defaults) are the reference: they define the grid centre and
        // the "initial" baseline, independent of what is currently written in appsettings.json.
        var initial = new AdaptiveFusionOptions();
        var tune = candidates.Where(c => c.Query.Split == tuneOn).ToList();
        var tunedV1 = await TuneAsync("v1", Grid(initial, wide: false).ToList(), tune, search, initial, tuneOn, ct);
        var tunedV2 = await TuneAsync("v2", Grid(initial, wide: true).ToList(), tune, search, initial, tuneOn, ct);

        // 2) Evaluate every method on every split (final numbers: evalOn).
        var methods = new (string Name, Func<Candidates, IReadOnlyList<string>> Rank)[]
        {
            ("Vector", c => c.Vector.Take(TopN).ToList()),
            ("FullText", c => c.FullText.Take(TopN).ToList()),
            ("HybridRrf", c => Rrf(c, search.RrfK)),
            ("HybridAdaptive(initial)", c => Adaptive(c, initial, search.RrfK)),
            ("HybridAdaptive(tuned v1)", c => Adaptive(c, tunedV1, search.RrfK)),
            ("HybridAdaptive(tuned v2)", c => Adaptive(c, tunedV2, search.RrfK))
        };

        var runRows = new List<object?[]>();
        var perQuery = new Dictionary<(string Method, string QueryId), Metrics>();
        foreach (var c in candidates)
        foreach (var (name, rank) in methods)
        {
            var ranking = rank(c);
            var m = Evaluate(c.Query, ranking);
            perQuery[(name, c.Query.Id)] = m;
            runRows.Add([c.Query.Id, c.Query.Split, c.Query.Kind, c.Query.Lang, name, m.Recall3, m.Recall5, m.Mrr10,
                string.Join('|', ranking.Take(5)), string.Join('|', c.Query.Relevant), c.Query.Query]);
        }
        Csv.Write(Path.Combine(paths.Results, "retrieval_runs.csv"),
            ["query_id", "split", "kind", "lang", "method", "recall3", "recall5", "mrr10", "top5", "relevant", "query"], runRows);

        var summaryRows = new List<object?[]>();
        foreach (var split in new[] { evalOn, tuneOn, "all" })
        foreach (var kind in new[] { "all", "keyword", "descriptive" })
        foreach (var (name, _) in methods)
        {
            var ids = candidates.Where(c => (split == "all" || c.Query.Split == split) && (kind == "all" || c.Query.Kind == kind))
                .Select(c => c.Query.Id).ToList();
            if (ids.Count == 0) continue;
            var m = Mean(ids.Select(id => perQuery[(name, id)]));
            summaryRows.Add([split, kind, name, ids.Count, m.Recall3, m.Recall5, m.Mrr10]);
        }
        Csv.Write(Path.Combine(paths.Results, "summary_retrieval.csv"),
            ["split", "kind", "method", "queries", "recall3", "recall5", "mrr10"], summaryRows);

        // 3) Paired bootstrap (MRR@10) on the evaluation split.
        var evalIds = candidates.Where(c => c.Query.Split == evalOn).Select(c => c.Query.Id).ToList();
        var comparisons = new[]
        {
            ("HybridAdaptive(tuned v1)", "HybridRrf"),
            ("HybridAdaptive(tuned v2)", "HybridRrf"),
            ("HybridAdaptive(tuned v2)", "Vector"),
            ("HybridRrf", "Vector")
        };
        var bootstrap = comparisons.Select(pair =>
        {
            var diffs = evalIds.Select(id => perQuery[(pair.Item1, id)].Mrr10 - perQuery[(pair.Item2, id)].Mrr10).ToArray();
            var (low, high, pLeq0) = Bootstrap(diffs, 10_000, seed: 42);
            return new
            {
                comparison = $"{pair.Item1} - {pair.Item2}",
                split = evalOn,
                queries = diffs.Length,
                meanDifference = diffs.Length == 0 ? 0 : diffs.Average(),
                ci95 = new[] { low, high },
                pValueOneSided = pLeq0
            };
        }).ToList();
        await File.WriteAllTextAsync(Path.Combine(paths.Results, "retrieval_bootstrap.json"),
            JsonSerializer.Serialize(new { metric = "MRR@10", resamples = 10_000, comparisons = bootstrap },
                new JsonSerializerOptions { WriteIndented = true }), ct);

        foreach (var row in summaryRows.Where(r => (string)r[0]! == evalOn))
            logger.LogInformation("{Split} {Kind,-11} {Method,-24} R@3={R3:0.000} R@5={R5:0.000} MRR@10={Mrr:0.000} (n={N})",
                row[0], row[1], row[2], row[4], row[5], row[6], row[3]);
        foreach (var b in bootstrap)
            logger.LogInformation("Bootstrap {Comparison}: ΔMRR@10 = {Diff:0.000}, 95% CI [{Low:0.000}; {High:0.000}], p(Δ≤0) = {P:0.000}",
                b.comparison, b.meanDifference, b.ci95[0], b.ci95[1], b.pValueOneSided);
    }

    private async Task<AdaptiveFusionOptions> TuneAsync(
        string name, List<AdaptiveFusionOptions> grid, List<Candidates> tune, SearchOptions search,
        AdaptiveFusionOptions initial, string tuneOn, CancellationToken ct)
    {
        var rows = new List<object?[]>();
        (AdaptiveFusionOptions Params, Metrics M)? best = null;

        foreach (var p in grid)
        {
            var m = Mean(tune.Select(c => Evaluate(c, Adaptive(c, p, search.RrfK))));
            rows.Add([p.A, p.B, p.C, p.N0, p.E, p.Beta, p.MinWeight, m.Mrr10, m.Recall3, m.Recall5]);
            if (best is null || IsBetter(m, p, best.Value.M, best.Value.Params, initial))
                best = (p, m);
        }

        var tuned = best!.Value.Params;
        Csv.Write(Path.Combine(paths.Results, $"retrieval_grid_{name}.csv"),
            ["a", "b", "c", "n0", "e", "beta", "min_weight", "mrr10", "recall3", "recall5"], rows);
        await File.WriteAllTextAsync(Path.Combine(paths.Results, $"retrieval_tuned_params_{name}.json"),
            JsonSerializer.Serialize(new
            {
                grid = name,
                tunedOn = tuneOn,
                queries = tune.Count,
                combinations = grid.Count,
                selection = "max MRR@10 on dev; ties: Recall@3, then closest to defaults",
                tuned.A, tuned.B, tuned.C, tuned.N0, tuned.E, tuned.Beta, tuned.MinWeight, tuned.MaxWeight,
                devMrr10 = best.Value.M.Mrr10, devRecall3 = best.Value.M.Recall3
            }, new JsonSerializerOptions { WriteIndented = true }), ct);

        logger.LogInformation("Grid {Grid}: tuned on {Split} ({Count} queries, {Combinations} combinations): a={A} b={B} c={C} n0={N0} e={E} β={Beta} min={Min}, dev MRR@10={Mrr:0.000}",
            name, tuneOn, tune.Count, grid.Count, tuned.A, tuned.B, tuned.C, tuned.N0, tuned.E, tuned.Beta, tuned.MinWeight, best.Value.M.Mrr10);
        return tuned;
    }

    // ── Candidates ───────────────────────────────────────────────────────────

    private async Task<List<Candidates>> CollectCandidatesAsync(List<RetrievalQuery> queries, int candidates, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<IKnowledgeSearch>();
        var store = scope.ServiceProvider.GetRequiredService<KnowledgeStore>();
        var snapshot = await store.GetAsync(ct);
        if (snapshot.Chunks.Count == 0)
            throw new InvalidOperationException("The evaluation knowledge base is empty. Run the 'index' command first.");
        if (!snapshot.HasVectors)
            throw new InvalidOperationException("Some chunks have no embeddings. Run 'index' again when the embedding API is available.");

        var categories = snapshot.Chunks.ToDictionary(c => c.Key, c => c.Category);
        var result = new List<Candidates>();
        var mismatches = 0;

        foreach (var q in queries)
        {
            var vector = await search.SearchAsync(q.Query, candidates, KnowledgeSearchMode.Vector, ct);
            if (vector.VectorUnavailable)
                throw new InvalidOperationException($"Query embedding failed for {q.Id}; check the embedding quota and retry.");
            var text = await search.SearchAsync(q.Query, candidates, KnowledgeSearchMode.FullText, ct);
            var c = new Candidates(q, [.. vector.Hits.Select(h => h.Key)], [.. text.Hits.Select(h => h.Key)],
                QueryFeatures.Extract(q.Query, snapshot.Vocabulary), categories);

            // Sanity check: local fusion = application's HybridAdaptive with the configured parameters.
            var app = await search.SearchAsync(q.Query, TopN, KnowledgeSearchMode.HybridAdaptive, ct);
            if (!app.Hits.Select(h => h.Key).SequenceEqual(Adaptive(c, aiOptions.Value.Search.Adaptive, aiOptions.Value.Search.RrfK)))
                mismatches++;

            result.Add(c);
        }

        if (mismatches > 0)
            logger.LogWarning("Local fusion differs from the application for {Count} queries", mismatches);
        else
            logger.LogInformation("Collected candidates for {Count} queries; local fusion matches the application", result.Count);
        return result;
    }

    // ── Fusion ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> Rrf(Candidates c, int k) => Fuse(c, k, 1, 1, _ => 1);

    private static IReadOnlyList<string> Adaptive(Candidates c, AdaptiveFusionOptions p, int k)
    {
        var (wFt, wVec) = c.Features.Weights(p);
        var category = c.Features.PredictedCategory;
        return Fuse(c, k, wVec, wFt, key => category is not null && c.Categories.GetValueOrDefault(key) == category ? 1 + p.Beta : 1);
    }

    private static IReadOnlyList<string> Fuse(Candidates c, int k, double wVec, double wFt, Func<string, double> boost)
    {
        // Ids must order like database ids for identical tie-breaking: use the chunk order of the knowledge base
        // (chunks are inserted document by document in file order, so database ids follow the same order).
        var order = c.Categories.Keys.Select((key, i) => (key, i)).ToDictionary(x => x.key, x => x.i);
        int Id(string key) => order.GetValueOrDefault(key, int.MaxValue);
        var byId = c.Vector.Concat(c.FullText).Distinct().ToDictionary(Id, key => key);

        return HybridFusion.Fuse(
                c.Vector.Select(Id).ToList(), c.FullText.Select(Id).ToList(), k, wVec, wFt,
                id => boost(byId[id]))
            .Take(TopN)
            .Select(x => byId[x.Id])
            .ToList();
    }

    /// <param name="minWeights">Values of the lower bound of w_ft; overrides the default set (used to keep BM25 in every query).</param>
    internal static IEnumerable<AdaptiveFusionOptions> Grid(AdaptiveFusionOptions defaults, bool wide, double[]? minWeights = null)
    {
        double[] aValues = wide ? [0.2, 0.3, 0.4, 0.5, 0.6] : [0.3, 0.4, 0.5, 0.6];
        double[] cValues = wide ? [0.0, 0.03, 0.06, 0.1, 0.15, 0.2, 0.3] : [0.0, 0.03, 0.06, 0.1];
        int[] n0Values = wide ? [2, 3, 4, 6] : [3, 4, 6];
        double[] minValues = minWeights ?? (wide ? [0.0, 0.05, defaults.MinWeight] : [defaults.MinWeight]);

        foreach (var a in aValues)
        foreach (var b in new[] { 0.0, 0.2, 0.4, 0.6 })
        foreach (var c in cValues)
        foreach (var n0 in n0Values)
        foreach (var e in new[] { 0.0, 0.1, 0.2 })
        foreach (var beta in new[] { 0.0, 0.15, 0.3, 0.5 })
        foreach (var min in minValues)
            yield return new AdaptiveFusionOptions
            {
                A = a, B = b, C = c, N0 = n0, E = e, Beta = beta,
                MinWeight = min, MaxWeight = defaults.MaxWeight
            };
    }

    internal static bool IsBetter(Metrics m, AdaptiveFusionOptions p, Metrics bestM, AdaptiveFusionOptions bestP, AdaptiveFusionOptions defaults)
    {
        const double eps = 1e-9;
        if (m.Mrr10 > bestM.Mrr10 + eps) return true;
        if (m.Mrr10 < bestM.Mrr10 - eps) return false;
        if (m.Recall3 > bestM.Recall3 + eps) return true;
        if (m.Recall3 < bestM.Recall3 - eps) return false;
        return Distance(p, defaults) < Distance(bestP, defaults);
    }

    private static double Distance(AdaptiveFusionOptions p, AdaptiveFusionOptions d) =>
        Math.Abs(p.A - d.A) + Math.Abs(p.B - d.B) + Math.Abs(p.C - d.C) * 5 + Math.Abs(p.N0 - d.N0) * 0.1
        + Math.Abs(p.E - d.E) + Math.Abs(p.Beta - d.Beta) + Math.Abs(p.MinWeight - d.MinWeight);

    // ── Metrics ──────────────────────────────────────────────────────────────

    private static Metrics Evaluate(Candidates c, IReadOnlyList<string> ranking) => Evaluate(c.Query, ranking);

    internal static Metrics Evaluate(RetrievalQuery q, IReadOnlyList<string> ranking)
    {
        var relevant = q.Relevant.ToHashSet();
        double Recall(int k) => relevant.Count == 0 ? 0 : ranking.Take(k).Count(relevant.Contains) / (double)relevant.Count;
        var firstHit = ranking.Take(TopN).Select((key, i) => (key, i)).FirstOrDefault(x => relevant.Contains(x.key));
        var rr = firstHit.key is null ? 0 : 1.0 / (firstHit.i + 1);
        return new Metrics(Recall(3), Recall(5), rr);
    }

    internal static Metrics Mean(IEnumerable<Metrics> metrics)
    {
        var list = metrics.ToList();
        return list.Count == 0
            ? new Metrics(0, 0, 0)
            : new Metrics(list.Average(m => m.Recall3), list.Average(m => m.Recall5), list.Average(m => m.Mrr10));
    }

    /// <summary>Percentile bootstrap of the mean difference; returns 95% CI and P(mean ≤ 0).</summary>
    internal static (double Low, double High, double PLeq0) Bootstrap(double[] diffs, int resamples, int seed)
    {
        if (diffs.Length == 0) return (0, 0, 1);
        var random = new Random(seed);
        var means = new double[resamples];
        for (var r = 0; r < resamples; r++)
        {
            var sum = 0.0;
            for (var i = 0; i < diffs.Length; i++) sum += diffs[random.Next(diffs.Length)];
            means[r] = sum / diffs.Length;
        }
        Array.Sort(means);
        return (means[(int)(0.025 * resamples)], means[(int)(0.975 * resamples) - 1], means.Count(m => m <= 0) / (double)resamples);
    }
}
