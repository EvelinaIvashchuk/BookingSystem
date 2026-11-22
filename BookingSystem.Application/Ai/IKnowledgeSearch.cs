namespace BookingSystem.Ai;

/// <summary>Retrieval method over the rental-policy knowledge base.</summary>
public enum KnowledgeSearchMode
{
    /// <summary>Cosine similarity of embeddings.</summary>
    Vector,

    /// <summary>MySQL FULLTEXT (natural language mode) over normalized text.</summary>
    FullText,

    /// <summary>Reciprocal Rank Fusion of both lists with equal weights (baseline).</summary>
    HybridRrf,

    /// <summary>RRF with query-dependent weights and a category boost (proposed method).</summary>
    HybridAdaptive
}

public interface IKnowledgeSearch
{
    /// <param name="query">User question in natural language.</param>
    /// <param name="k">Number of fragments to return.</param>
    /// <param name="mode"><c>null</c> means <c>Ai:Search:Mode</c>.</param>
    Task<KnowledgeSearchResult> SearchAsync(
        string query, int k, KnowledgeSearchMode? mode = null, CancellationToken ct = default);
}

/// <param name="VectorUnavailable">Embeddings could not be used (no key / provider error); hybrid modes fell back to full-text.</param>
public sealed record KnowledgeSearchResult(
    KnowledgeSearchMode Mode,
    IReadOnlyList<KnowledgeHit> Hits,
    QueryFeatureSnapshot? Features = null,
    bool VectorUnavailable = false);

/// <param name="Key">Stable fragment id, e.g. <c>deposit#2</c> (used in relevance judgments).</param>
/// <param name="VectorRank">1-based rank in the vector list, if the fragment was a candidate there.</param>
/// <param name="FullTextRank">1-based rank in the full-text list, if the fragment was a candidate there.</param>
public sealed record KnowledgeHit(
    string Key,
    string Category,
    string Title,
    string Content,
    double Score,
    int? VectorRank = null,
    int? FullTextRank = null);

/// <summary>Query features and the weights derived from them (for analysis and experiments).</summary>
public sealed record QueryFeatureSnapshot(
    int WordCount,
    double DomainTermShare,
    bool HasNumber,
    string? PredictedCategory,
    double FullTextWeight,
    double VectorWeight);
