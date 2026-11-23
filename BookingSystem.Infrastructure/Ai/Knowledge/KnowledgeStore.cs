using System.Runtime.InteropServices;
using BookingSystem.Data;
using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Ai.Knowledge;

/// <summary>
/// In-memory snapshot of the knowledge base (≈200 chunks): vectors for cosine search and the
/// domain vocabulary for query features. Reloaded after re-indexing.
/// </summary>
public sealed class KnowledgeStore
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<KnowledgeChunk>>> _loader;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile Snapshot? _snapshot;

    public KnowledgeStore(IServiceScopeFactory scopes) : this(async ct =>
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.KnowledgeChunks.AsNoTracking().OrderBy(c => c.DocumentId).ThenBy(c => c.ChunkIndex).ToListAsync(ct);
    })
    {
    }

    internal KnowledgeStore(Func<CancellationToken, Task<IReadOnlyList<KnowledgeChunk>>> loader) => _loader = loader;

    public sealed record IndexedChunk(int Id, string Key, string Category, string Title, string Content, float[]? Vector)
    {
        /// <summary>Stemmed text (<see cref="TextNormalizer.ToSearchText"/>) used by BM25.</summary>
        public string SearchText { get; init; } = string.Empty;
    }

    public sealed record Snapshot(
        IReadOnlyList<IndexedChunk> Chunks,
        IReadOnlyDictionary<int, IndexedChunk> ById,
        DomainVocabulary Vocabulary,
        bool HasVectors)
    {
        private Bm25Index? _bm25;

        /// <summary>BM25 index over <see cref="IndexedChunk.SearchText"/>, built on first use.</summary>
        public Bm25Index Bm25 => _bm25 ??= new Bm25Index(Chunks.Select(c => (c.Id, c.SearchText)));
    }

    public async Task<Snapshot> GetAsync(CancellationToken ct = default)
    {
        if (_snapshot is { } ready) return ready;

        await _lock.WaitAsync(ct);
        try
        {
            if (_snapshot is { } loaded) return loaded;

            var rows = await _loader(ct);
            var chunks = rows
                .Select(c => new IndexedChunk(c.Id, c.Key, c.Category, c.Title, c.Content, FromBytes(c.Embedding))
                {
                    SearchText = string.IsNullOrEmpty(c.SearchText) ? TextNormalizer.ToSearchText($"{c.Title}\n{c.Content}") : c.SearchText
                })
                .ToList();

            return _snapshot = new Snapshot(
                chunks,
                chunks.ToDictionary(c => c.Id),
                new DomainVocabulary(chunks.Select(c => c.Title).Distinct()),
                chunks.Count > 0 && chunks.All(c => c.Vector is not null));
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _snapshot = null;

    public static byte[] ToBytes(ReadOnlySpan<float> vector) => MemoryMarshal.AsBytes(vector).ToArray();

    public static float[]? FromBytes(byte[]? bytes) =>
        bytes is null || bytes.Length == 0 ? null : MemoryMarshal.Cast<byte, float>(bytes).ToArray();
}
