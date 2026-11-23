using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using BookingSystem.Ai;
using BookingSystem.Ai.Knowledge;
using BookingSystem.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Metrics = BookingSystem.Evaluation.RetrievalExperiment.Metrics;

namespace BookingSystem.Evaluation;

/// <summary>
/// E3 (k-fold version): knowledge-base search on the expanded knowledge base with k-fold cross-validation.
/// <list type="number">
/// <item>For every query the candidate lists are computed once: vector (top-20), own BM25 (top-20) and, for comparison
///       with the first E3 run, MySQL FULLTEXT (top-20).</item>
/// <item>Folds 1..k come from the dataset (stratified by kind and language). For every fold the six parameters of the
///       adaptive fusion are tuned on the other k−1 folds over the wide grid (fixed in advance, 20 160 combinations) and
///       the tuned fusion is evaluated on the held-out fold. Every query is therefore evaluated exactly once by a model
///       that has not seen it (out-of-fold).</item>
/// <item>Methods: Vector, BM25, MySQL FULLTEXT, HybridRrf (Vector + BM25, k = 60), HybridAdaptive with the initial
///       parameters, HybridAdaptive tuned by cross-validation with BM25 kept in every query (w_ft ≥ 0.1, what the
///       application uses) and, for comparison, without that constraint (free: w_ft may fall to 0).</item>
/// <item>95% bootstrap CIs of mean MRR@10 and paired bootstrap of differences (10 000 resamples over queries).</item>
/// <item>Parameters tuned on all queries are written separately — they are what the application uses, not what is
///       evaluated.</item>
/// </list>
/// </summary>
public sealed class RetrievalCvExperiment(
    IServiceScopeFactory scopes,
    IOptions<AiOptions> aiOptions,
    EvalPaths paths,
    ILogger<RetrievalCvExperiment> logger)
{
    private const int TopN = 10;

    private sealed record Item(RetrievalQuery Query, int[] Vector, int[] Bm25, int[] Bm25Prf, int[] MySql, QueryFeatures Features, HashSet<int> CategoryIds);

    /// <summary>Pseudo-relevance feedback for BM25 — the application's values, fixed in advance (not tuned here).</summary>
    private QueryExpansionOptions Prf => aiOptions.Value.Search.QueryExpansion;

    public async Task RunAsync(int folds, CancellationToken ct)
    {
        var queries = Datasets.LoadQrels(Path.Combine(paths.Datasets, "retrieval_qrels.json")).Queries;
        if (queries.Any(q => q.Fold < 1 || q.Fold > folds))
            throw new InvalidOperationException($"Every query needs a fold 1..{folds} (tools/retrieval/make_qrels.py).");

        var search = aiOptions.Value.Search;
        var (items, keys, chunkCount) = await CollectAsync(queries, search.Candidates, ct);
        logger.LogInformation("E3-CV: {Queries} queries, {Chunks} chunks, {Folds} folds", items.Count, chunkCount, folds);

        var initial = new AdaptiveFusionOptions();
        // Hybrid constraint (fixed in advance): BM25 keeps at least 10% of the weight for every query, otherwise the
        // tuning drives w_ft to 0 for long queries and the "hybrid" degenerates into vector search.
        var grid = RetrievalExperiment.Grid(initial, wide: true, HybridMinWeights).ToList();
        var freeGrid = RetrievalExperiment.Grid(initial, wide: true).ToList();

        // ---- out-of-fold tuning and evaluation of the adaptive fusion (constrained and, for comparison, free)
        var tunedPerQuery = new Dictionary<string, AdaptiveFusionOptions>();
        var freePerQuery = new Dictionary<string, AdaptiveFusionOptions>();
        var prfPerQuery = new Dictionary<string, AdaptiveFusionOptions>();
        var foldRows = new List<object>();
        for (var f = 1; f <= folds; f++)
        {
            var train = items.Where(i => i.Query.Fold != f).ToList();
            var test = items.Where(i => i.Query.Fold == f).ToList();
            var (best, trainMetrics) = Tune(grid, train, keys, search.RrfK, initial);
            var (free, _) = Tune(freeGrid, train, keys, search.RrfK, initial);
            var (prf, _) = Tune(grid, train, keys, search.RrfK, initial, usePrf: true);
            var testMetrics = RetrievalExperiment.Mean(test.Select(i => Evaluate(i, Adaptive(i, best, search.RrfK), keys)));
            var freeMetrics = RetrievalExperiment.Mean(test.Select(i => Evaluate(i, Adaptive(i, free, search.RrfK), keys)));
            var prfMetrics = RetrievalExperiment.Mean(test.Select(i => Evaluate(i, Adaptive(i, prf, search.RrfK, usePrf: true), keys)));
            foreach (var i in test)
            {
                tunedPerQuery[i.Query.Id] = best;
                freePerQuery[i.Query.Id] = free;
                prfPerQuery[i.Query.Id] = prf;
            }
            foldRows.Add(new
            {
                fold = f, trainQueries = train.Count, testQueries = test.Count,
                best.A, best.B, best.C, best.N0, best.E, best.Beta, best.MinWeight,
                trainMrr10 = trainMetrics.Mrr10, testMrr10 = testMetrics.Mrr10,
                free = new { free.A, free.B, free.C, free.N0, free.E, free.Beta, free.MinWeight, testMrr10 = freeMetrics.Mrr10 },
                prf = new { prf.A, prf.B, prf.C, prf.N0, prf.E, prf.Beta, prf.MinWeight, testMrr10 = prfMetrics.Mrr10 }
            });
            logger.LogInformation("Fold {Fold}: tuned on {Train} queries (MRR@10 {TrainMrr:0.000}), held-out {Test} queries MRR@10 {TestMrr:0.000} (free: {FreeMrr:0.000}, PRF: {PrfMrr:0.000}); a={A} b={B} c={C} n0={N0} e={E} β={Beta} min={Min}",
                f, train.Count, trainMetrics.Mrr10, test.Count, testMetrics.Mrr10, freeMetrics.Mrr10, prfMetrics.Mrr10, best.A, best.B, best.C, best.N0, best.E, best.Beta, best.MinWeight);
        }

        // Parameters for the application: tuned on all queries (not used for any reported metric).
        var (all, allMetrics) = Tune(grid, items, keys, search.RrfK, initial);
        var (allFree, _) = Tune(freeGrid, items, keys, search.RrfK, initial);
        var (allPrf, allPrfMetrics) = Tune(grid, items, keys, search.RrfK, initial, usePrf: true);
        var weightStats = new
        {
            constrained = WeightStats(items, all),
            free = WeightStats(items, allFree),
            prf = WeightStats(items, allPrf)
        };

        // ---- all methods, per query
        var methods = new (string Name, Func<Item, IReadOnlyList<int>> Rank)[]
        {
            ("Vector", i => i.Vector.Take(TopN).ToList()),
            ("BM25", i => i.Bm25.Take(TopN).ToList()),
            ("BM25+PRF", i => i.Bm25Prf.Take(TopN).ToList()),
            ("MySqlFullText", i => i.MySql.Take(TopN).ToList()),
            ("HybridRrf", i => Fuse(i, search.RrfK, 1, 1, null)),
            ("HybridAdaptive(initial)", i => Adaptive(i, initial, search.RrfK)),
            ("HybridAdaptive(cv)", i => Adaptive(i, tunedPerQuery[i.Query.Id], search.RrfK)),
            ("HybridAdaptive(cv,free)", i => Adaptive(i, freePerQuery[i.Query.Id], search.RrfK)),
            ("HybridAdaptive(cv,PRF)", i => Adaptive(i, prfPerQuery[i.Query.Id], search.RrfK, usePrf: true))
        };

        var perQuery = new Dictionary<(string Method, string Id), Metrics>();
        var runRows = new List<object?[]>();
        foreach (var i in items)
        foreach (var (name, rank) in methods)
        {
            var ranking = rank(i);
            var m = Evaluate(i, ranking, keys);
            perQuery[(name, i.Query.Id)] = m;
            runRows.Add([i.Query.Id, i.Query.Fold, i.Query.Kind, i.Query.Lang, i.Query.Origin, name, m.Recall3, m.Recall5, m.Mrr10,
                string.Join('|', ranking.Take(5).Select(id => keys[id])), string.Join('|', i.Query.Relevant), i.Query.Query]);
        }
        Csv.Write(Path.Combine(paths.Results, "retrieval_cv_runs.csv"),
            ["query_id", "fold", "kind", "lang", "origin", "method", "recall3", "recall5", "mrr10", "top5", "relevant", "query"], runRows);

        var groups = new (string Name, Func<RetrievalQuery, bool> Filter)[]
        {
            ("all", _ => true), ("keyword", q => q.Kind == "keyword"), ("descriptive", q => q.Kind == "descriptive"),
            ("uk", q => q.Lang == "uk"), ("en", q => q.Lang == "en"),
            ("original", q => q.Origin == "original"), ("expanded-kb", q => q.Origin != "original")
        };
        var summaryRows = new List<object?[]>();
        foreach (var (group, filter) in groups)
        foreach (var (name, _) in methods)
        {
            var ids = items.Where(i => filter(i.Query)).Select(i => i.Query.Id).ToList();
            if (ids.Count == 0) continue;
            var ms = ids.Select(id => perQuery[(name, id)]).ToList();
            var mean = RetrievalExperiment.Mean(ms);
            var (low, high, _) = RetrievalExperiment.Bootstrap([.. ms.Select(m => m.Mrr10)], 10_000, seed: 7);
            summaryRows.Add([group, name, ids.Count, mean.Recall3, mean.Recall5, mean.Mrr10, low, high]);
        }
        Csv.Write(Path.Combine(paths.Results, "summary_retrieval_cv.csv"),
            ["group", "method", "queries", "recall3", "recall5", "mrr10", "mrr10_ci_low", "mrr10_ci_high"], summaryRows);

        var comparisons = new[]
        {
            ("HybridAdaptive(cv)", "Vector"), ("HybridAdaptive(cv)", "HybridRrf"), ("HybridAdaptive(cv)", "BM25"),
            ("HybridRrf", "Vector"), ("BM25", "MySqlFullText"), ("HybridAdaptive(cv)", "HybridAdaptive(initial)"),
            ("HybridAdaptive(cv)", "HybridAdaptive(cv,free)"), ("HybridAdaptive(cv,free)", "Vector"),
            ("BM25+PRF", "BM25"), ("HybridAdaptive(cv,PRF)", "HybridAdaptive(cv)"), ("HybridAdaptive(cv,PRF)", "Vector")
        };
        var bootstrap = new List<object>();
        foreach (var (group, filter) in groups.Take(5))
        foreach (var (a, b) in comparisons)
        {
            var ids = items.Where(i => filter(i.Query)).Select(i => i.Query.Id).ToList();
            var diffs = ids.Select(id => perQuery[(a, id)].Mrr10 - perQuery[(b, id)].Mrr10).ToArray();
            var (low, high, pLeq0) = RetrievalExperiment.Bootstrap(diffs, 10_000, seed: 42);
            bootstrap.Add(new
            {
                group, comparison = $"{a} - {b}", queries = diffs.Length, meanDifference = diffs.Average(),
                ci95 = new[] { low, high }, pValueOneSided = pLeq0,
                better = diffs.Count(d => d > 1e-9), worse = diffs.Count(d => d < -1e-9)
            });
        }

        var json = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(paths.Results, "retrieval_cv_bootstrap.json"),
            JsonSerializer.Serialize(new { metric = "MRR@10", resamples = 10_000, comparisons = bootstrap }, json), ct);
        await File.WriteAllTextAsync(Path.Combine(paths.Results, "retrieval_cv_folds.json"),
            JsonSerializer.Serialize(new
            {
                folds, grid = "wide (fixed in advance)", combinations = grid.Count,
                hybridConstraint = $"MinWeight ∈ {{{string.Join(", ", HybridMinWeights.Select(w => w.ToString(CultureInfo.InvariantCulture)))}}}",
                freeCombinations = freeGrid.Count, perFold = foldRows
            }, json), ct);
        await File.WriteAllTextAsync(Path.Combine(paths.Results, "retrieval_tuned_params_cv.json"),
            JsonSerializer.Serialize(new
            {
                tunedOn = "all queries (for the application; reported metrics are out-of-fold)", queries = items.Count,
                all.A, all.B, all.C, all.N0, all.E, all.Beta, all.MinWeight, all.MaxWeight, mrr10OnAll = allMetrics.Mrr10,
                fullTextWeight = weightStats,
                free = new { allFree.A, allFree.B, allFree.C, allFree.N0, allFree.E, allFree.Beta, allFree.MinWeight },
                prf = new
                {
                    feedbackDocs = Prf.FeedbackDocs, terms = Prf.Terms, weight = Prf.Weight,
                    allPrf.A, allPrf.B, allPrf.C, allPrf.N0, allPrf.E, allPrf.Beta, allPrf.MinWeight, allPrf.MaxWeight,
                    mrr10OnAll = allPrfMetrics.Mrr10
                }
            }, json), ct);

        foreach (var row in summaryRows.Where(r => (string)r[0]! is "all" or "keyword" or "descriptive" or "en"))
            logger.LogInformation("{Group,-11} {Method,-24} R@3={R3:0.000} R@5={R5:0.000} MRR@10={Mrr:0.000} [{Lo:0.000}; {Hi:0.000}] (n={N})",
                row[0], row[1], row[3], row[4], row[5], row[6], row[7], row[2]);
        foreach (dynamic b in bootstrap.Where(x => ((dynamic)x).group == "all"))
            logger.LogInformation("Δ {Comparison}: {Diff:0.000} [{Lo:0.000}; {Hi:0.000}], better {Better} / worse {Worse}",
                (string)b.comparison, (double)b.meanDifference, (double)b.ci95[0], (double)b.ci95[1], (int)b.better, (int)b.worse);
        logger.LogInformation("Application parameters (tuned on all queries): a={A} b={B} c={C} n0={N0} e={E} β={Beta} min={Min}",
            all.A, all.B, all.C, all.N0, all.E, all.Beta, all.MinWeight);
        logger.LogInformation("Application parameters with PRF: a={A} b={B} c={C} n0={N0} e={E} β={Beta} min={Min}",
            allPrf.A, allPrf.B, allPrf.C, allPrf.N0, allPrf.E, allPrf.Beta, allPrf.MinWeight);
        logger.LogInformation("w_ft over queries: constrained {Constrained}; free {Free}; PRF {Prf}",
            JsonSerializer.Serialize(weightStats.constrained), JsonSerializer.Serialize(weightStats.free), JsonSerializer.Serialize(weightStats.prf));
    }

    private static readonly double[] HybridMinWeights = [0.1, 0.15, 0.2];

    /// <summary>Mean BM25 weight and the share of queries where BM25 is switched off (w_ft = 0).</summary>
    private static object WeightStats(List<Item> items, AdaptiveFusionOptions p)
    {
        var w = items.Select(i => i.Features.Weights(p).FullText).ToList();
        return new { mean = Math.Round(w.Average(), 3), min = w.Min(), max = w.Max(), shareZero = Math.Round(w.Count(x => x <= 1e-9) / (double)w.Count, 3) };
    }

    private static (AdaptiveFusionOptions Best, Metrics Metrics) Tune(
        List<AdaptiveFusionOptions> grid, List<Item> items, Dictionary<int, string> keys, int k, AdaptiveFusionOptions initial, bool usePrf = false)
    {
        var results = new ConcurrentBag<(int Index, Metrics M)>();
        Parallel.For(0, grid.Count, g =>
            results.Add((g, RetrievalExperiment.Mean(items.Select(i => Evaluate(i, Adaptive(i, grid[g], k, usePrf), keys))))));

        (AdaptiveFusionOptions P, Metrics M)? best = null;
        foreach (var (index, m) in results.OrderBy(r => r.Index))
            if (best is null || RetrievalExperiment.IsBetter(m, grid[index], best.Value.M, best.Value.P, initial))
                best = (grid[index], m);
        return (best!.Value.P, best.Value.M);
    }

    private static IReadOnlyList<int> Adaptive(Item i, AdaptiveFusionOptions p, int k, bool usePrf = false)
    {
        var (wFt, wVec) = i.Features.Weights(p);
        return Fuse(i, k, wVec, wFt, id => i.CategoryIds.Contains(id) ? 1 + p.Beta : 1, usePrf);
    }

    private static IReadOnlyList<int> Fuse(Item i, int k, double wVec, double wFt, Func<int, double>? boost, bool usePrf = false) =>
        HybridFusion.Fuse(i.Vector, usePrf ? i.Bm25Prf : i.Bm25, k, wVec, wFt, boost).Take(TopN).Select(x => x.Id).ToList();

    private static Metrics Evaluate(Item i, IReadOnlyList<int> ranking, Dictionary<int, string> keys) =>
        RetrievalExperiment.Evaluate(i.Query, [.. ranking.Select(id => keys[id])]);

    private async Task<(List<Item> Items, Dictionary<int, string> Keys, int Chunks)> CollectAsync(
        List<RetrievalQuery> queries, int candidates, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var search = scope.ServiceProvider.GetRequiredService<IKnowledgeSearch>();
        var store = scope.ServiceProvider.GetRequiredService<KnowledgeStore>();
        var mySql = new MySqlFullTextIndex(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        var snapshot = await store.GetAsync(ct);
        if (snapshot.Chunks.Count == 0 || !snapshot.HasVectors)
            throw new InvalidOperationException("The evaluation knowledge base is empty or has no embeddings. Run the 'index' command first.");

        var keys = snapshot.Chunks.ToDictionary(c => c.Id, c => c.Key);
        var idByKey = snapshot.Chunks.ToDictionary(c => c.Key, c => c.Id);
        var unknown = queries.SelectMany(q => q.Relevant).Where(r => !idByKey.ContainsKey(r)).Distinct().ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException("Relevant chunks missing from the index (re-run 'index'): " + string.Join(", ", unknown));

        var items = new List<Item>();
        foreach (var q in queries)
        {
            var vector = await search.SearchAsync(q.Query, candidates, KnowledgeSearchMode.Vector, ct);
            if (vector.VectorUnavailable)
                throw new InvalidOperationException($"Query embedding failed for {q.Id}; check the embedding quota and retry.");
            var features = QueryFeatures.Extract(q.Query, snapshot.Vocabulary);
            var stems = string.Join(' ', features.Stems);
            var bm25 = snapshot.Bm25.Search(stems, candidates).Select(x => x.Id).ToArray();
            var vectorIds = vector.Hits.Select(h => idByKey[h.Key]).ToArray();
            var bm25Prf = snapshot.Bm25.Search(snapshot.Bm25.Expand(stems, vectorIds.Take(Prf.FeedbackDocs), Prf.Terms, Prf.Weight), candidates)
                .Select(x => x.Id).ToArray();
            var mysql = (await mySql.SearchAsync(stems, candidates, ct)).Select(x => x.Id).ToArray();
            var category = features.PredictedCategory;
            var categoryIds = category is null
                ? []
                : snapshot.Chunks.Where(c => c.Category == category).Select(c => c.Id).ToHashSet();
            items.Add(new Item(q, vectorIds, bm25, bm25Prf, mysql, features, categoryIds));
        }
        return (items, keys, snapshot.Chunks.Count);
    }
}
