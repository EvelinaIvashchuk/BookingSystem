using System.ClientModel;
using System.Numerics.Tensors;
using Microsoft.Extensions.Options;

namespace BookingSystem.Ai.Knowledge;

/// <summary>
/// Four retrieval methods over the knowledge base:
/// <list type="bullet">
/// <item><see cref="KnowledgeSearchMode.Vector"/> — cosine similarity of embeddings;</item>
/// <item><see cref="KnowledgeSearchMode.FullText"/> — MySQL FULLTEXT over normalized text;</item>
/// <item><see cref="KnowledgeSearchMode.HybridRrf"/> — classic RRF of the top-N of both (baseline);</item>
/// <item><see cref="KnowledgeSearchMode.HybridAdaptive"/> — RRF with query-dependent weights and
///       a soft category boost (proposed method).</item>
/// </list>
/// If embeddings are unavailable, hybrid modes degrade to full-text and the result says so.
/// </summary>
public sealed class KnowledgeSearch(
    KnowledgeStore store,
    IFullTextIndex fullText,
    IEmbeddingGeneratorFactory embeddingFactory,
    QueryEmbeddingCache embeddingCache,
    IOptions<AiOptions> options,
    ILogger<KnowledgeSearch> logger) : IKnowledgeSearch
{
    private readonly SearchOptions _search = options.Value.Search;

    public async Task<KnowledgeSearchResult> SearchAsync(
        string query, int k, KnowledgeSearchMode? mode = null, CancellationToken ct = default)
    {
        var method = mode ?? _search.Mode;
        var snapshot = await store.GetAsync(ct);
        var features = QueryFeatures.Extract(query, snapshot.Vocabulary);
        var candidates = Math.Max(_search.Candidates, k);

        IReadOnlyList<(int Id, double Score)> vector = [];
        var vectorUnavailable = false;
        if (method != KnowledgeSearchMode.FullText)
        {
            var ranked = await VectorRankingAsync(query, snapshot, candidates, ct);
            vectorUnavailable = ranked is null;
            vector = ranked ?? [];
        }

        IReadOnlyList<(int Id, double Score)> text = method == KnowledgeSearchMode.Vector
            ? []
            : await fullText.SearchAsync(string.Join(' ', features.Stems), candidates, ct);

        var (wFt, wVec) = features.Weights(_search.Adaptive);
        var boost = 1 + _search.Adaptive.Beta;

        IReadOnlyList<HybridFusion.FusedItem> ranking = method switch
        {
            KnowledgeSearchMode.Vector =>
                vector.Select((v, i) => new HybridFusion.FusedItem(v.Id, v.Score, i + 1, null)).ToList(),
            KnowledgeSearchMode.FullText =>
                text.Select((t, i) => new HybridFusion.FusedItem(t.Id, t.Score, null, i + 1)).ToList(),
            KnowledgeSearchMode.HybridRrf =>
                HybridFusion.Fuse(Ids(vector), Ids(text), _search.RrfK),
            _ => HybridFusion.Fuse(
                Ids(vector), Ids(text), _search.RrfK, wVec, wFt,
                id => features.PredictedCategory is { } category
                      && snapshot.ById.TryGetValue(id, out var chunk) && chunk.Category == category
                    ? boost
                    : 1)
        };

        var hits = ranking
            .Where(r => snapshot.ById.ContainsKey(r.Id))
            .Take(k)
            .Select(r =>
            {
                var chunk = snapshot.ById[r.Id];
                return new KnowledgeHit(chunk.Key, chunk.Category, chunk.Title, chunk.Content, r.Score, r.VectorRank, r.FullTextRank);
            })
            .ToList();

        logger.LogInformation(
            "Knowledge search {Mode} \"{Query}\": n={Words}, d={DomainShare:0.00}, num={HasNumber}, category={Category}, w_ft={FullTextWeight:0.00} -> [{Keys}]",
            method, query, features.WordCount, features.DomainTermShare, features.HasNumber,
            features.PredictedCategory ?? "-", wFt, string.Join(", ", hits.Select(h => h.Key)));

        return new KnowledgeSearchResult(
            method,
            hits,
            new QueryFeatureSnapshot(features.WordCount, features.DomainTermShare, features.HasNumber,
                features.PredictedCategory, wFt, wVec),
            vectorUnavailable);
    }

    /// <summary>Top-N chunks by cosine similarity; <c>null</c> when vectors cannot be computed.</summary>
    private async Task<IReadOnlyList<(int Id, double Score)>?> VectorRankingAsync(
        string query, KnowledgeStore.Snapshot snapshot, int limit, CancellationToken ct)
    {
        if (!snapshot.Chunks.Any(c => c.Vector is not null)) return null;

        float[] queryVector;
        try
        {
            var generator = embeddingFactory.Create();
            queryVector = await embeddingCache.GetOrAddAsync(embeddingFactory.ModelId, query, async () =>
                (await generator.GenerateAsync([query], cancellationToken: ct))[0].Vector.ToArray());
        }
        catch (Exception ex) when (ex is AiNotConfiguredException or ClientResultException or HttpRequestException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Query embedding failed; vector search is unavailable for this query");
            return null;
        }

        return snapshot.Chunks
            .Where(c => c.Vector is { } v && v.Length == queryVector.Length)
            .Select(c => (c.Id, Score: (double)TensorPrimitives.CosineSimilarity(queryVector, c.Vector!)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToList();
    }

    private static IReadOnlyList<int> Ids(IReadOnlyList<(int Id, double Score)> list) => list.Select(x => x.Id).ToList();
}
