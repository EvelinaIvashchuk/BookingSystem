namespace BookingSystem.Ai.Knowledge;

/// <summary>
/// Okapi BM25 over the stemmed text of knowledge chunks (own implementation, replaces MySQL FULLTEXT ranking):
/// <code>
/// score(D, Q) = Σ_{t ∈ Q} IDF(t) · tf(t, D) · (k1 + 1) / (tf(t, D) + k1 · (1 − b + b · |D| / avgdl)),
/// IDF(t) = ln(1 + (N − df(t) + 0.5) / (df(t) + 0.5))
/// </code>
/// Terms are the stems produced by <see cref="TextNormalizer"/>, the same for documents and queries. Repeated query
/// terms count once. Ties are broken by chunk id, as in the MySQL query it replaces.
/// </summary>
public sealed class Bm25Index
{
    public const double DefaultK1 = 1.2;
    public const double DefaultB = 0.75;

    private readonly int[] _ids;
    private readonly Dictionary<string, int>[] _termFrequencies;
    private readonly int[] _lengths;
    private readonly double _averageLength;
    private readonly Dictionary<string, double> _idf;
    private readonly Dictionary<string, List<int>> _postings;
    private readonly double _k1;
    private readonly double _b;

    public Bm25Index(IEnumerable<(int Id, string SearchText)> documents, double k1 = DefaultK1, double b = DefaultB)
    {
        var docs = documents.ToList();
        _k1 = k1;
        _b = b;
        _ids = docs.Select(d => d.Id).ToArray();
        _termFrequencies = new Dictionary<string, int>[docs.Count];
        _lengths = new int[docs.Count];
        _postings = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var i = 0; i < docs.Count; i++)
        {
            var terms = Terms(docs[i].SearchText);
            _lengths[i] = terms.Length;
            var tf = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var term in terms)
                tf[term] = tf.GetValueOrDefault(term) + 1;
            _termFrequencies[i] = tf;
            foreach (var term in tf.Keys)
            {
                if (!_postings.TryGetValue(term, out var list))
                    _postings[term] = list = [];
                list.Add(i);
            }
        }

        _averageLength = docs.Count == 0 ? 0 : _lengths.Average();
        var n = docs.Count;
        _idf = _postings.ToDictionary(p => p.Key, p => Math.Log(1 + (n - p.Value.Count + 0.5) / (p.Value.Count + 0.5)), StringComparer.Ordinal);
    }

    public int Count => _ids.Length;

    public IReadOnlyList<(int Id, double Score)> Search(string searchText, int limit) =>
        Search(Terms(searchText).Distinct(StringComparer.Ordinal).ToDictionary(t => t, _ => 1.0, StringComparer.Ordinal), limit);

    /// <summary>BM25 with weighted query terms: score = Σ w(t) · bm25(t, D) (used by query expansion).</summary>
    public IReadOnlyList<(int Id, double Score)> Search(IReadOnlyDictionary<string, double> weightedTerms, int limit)
    {
        var scores = new Dictionary<int, double>();
        foreach (var (term, weight) in weightedTerms)
        {
            if (weight <= 0 || !_postings.TryGetValue(term, out var postings)) continue;
            var idf = _idf[term];
            foreach (var i in postings)
            {
                double tf = _termFrequencies[i][term];
                var norm = _averageLength == 0 ? 1 : 1 - _b + _b * _lengths[i] / _averageLength;
                scores[i] = scores.GetValueOrDefault(i) + weight * idf * tf * (_k1 + 1) / (tf + _k1 * norm);
            }
        }

        return scores
            .Select(kv => (Id: _ids[kv.Key], Score: kv.Value))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Pseudo-relevance feedback (RM3-like): the original terms with weight 1 plus the <paramref name="terms"/> terms
    /// that best describe the feedback documents, score(t) = Σ_d tf(t, d) / |d| · IDF(t), each with weight
    /// <paramref name="weight"/> · score(t) / max score. Terms already in the query keep weight 1.
    /// </summary>
    public IReadOnlyDictionary<string, double> Expand(string searchText, IEnumerable<int> feedbackIds, int terms, double weight)
    {
        var query = Terms(searchText).Distinct(StringComparer.Ordinal).ToDictionary(t => t, _ => 1.0, StringComparer.Ordinal);
        var index = _ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);

        var candidates = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var id in feedbackIds)
        {
            if (!index.TryGetValue(id, out var i) || _lengths[i] == 0) continue;
            foreach (var (term, tf) in _termFrequencies[i])
                if (!query.ContainsKey(term) && term.Length >= 3 && !term.All(char.IsDigit))
                    candidates[term] = candidates.GetValueOrDefault(term) + (double)tf / _lengths[i] * _idf[term];
        }

        var top = candidates.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).Take(terms).ToList();
        if (top.Count == 0) return query;
        var max = top[0].Value;
        foreach (var (term, score) in top)
            query[term] = weight * score / max;
        return query;
    }

    private static string[] Terms(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary><see cref="IFullTextIndex"/> backed by <see cref="Bm25Index"/> built from the in-memory knowledge snapshot.</summary>
public sealed class Bm25FullTextIndex(KnowledgeStore store) : IFullTextIndex
{
    public async Task<IReadOnlyList<(int Id, double Score)>> SearchAsync(string searchText, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return [];
        var snapshot = await store.GetAsync(ct);
        return snapshot.Bm25.Search(searchText, limit);
    }

    public async Task<IReadOnlyList<(int Id, double Score)>> SearchExpandedAsync(
        string searchText, IReadOnlyList<int> feedbackIds, QueryExpansionOptions expansion, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(searchText) && feedbackIds.Count == 0) return [];
        var snapshot = await store.GetAsync(ct);
        var terms = snapshot.Bm25.Expand(searchText, feedbackIds.Take(expansion.FeedbackDocs), expansion.Terms, expansion.Weight);
        return snapshot.Bm25.Search(terms, limit);
    }
}
