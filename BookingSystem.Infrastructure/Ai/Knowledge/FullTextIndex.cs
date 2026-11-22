using BookingSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Ai.Knowledge;

/// <summary>Full-text ranking of knowledge chunks (abstracted so tests can run without MySQL).</summary>
public interface IFullTextIndex
{
    /// <param name="searchText">Normalized query (<see cref="TextNormalizer.ToSearchText"/>).</param>
    Task<IReadOnlyList<(int Id, double Score)>> SearchAsync(string searchText, int limit, CancellationToken ct = default);
}

/// <summary>
/// MySQL FULLTEXT, natural language mode, over <c>KnowledgeChunks.SearchText</c>
/// (index <c>FT_KnowledgeChunks_SearchText</c>).
/// </summary>
public sealed class MySqlFullTextIndex(ApplicationDbContext db) : IFullTextIndex
{
    public async Task<IReadOnlyList<(int Id, double Score)>> SearchAsync(
        string searchText, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return [];

        var rows = await db.Database
            .SqlQuery<FullTextRow>($"""
                SELECT Id, MATCH(SearchText) AGAINST ({searchText} IN NATURAL LANGUAGE MODE) AS Score
                FROM KnowledgeChunks
                WHERE MATCH(SearchText) AGAINST ({searchText} IN NATURAL LANGUAGE MODE)
                ORDER BY Score DESC, Id
                LIMIT {limit}
                """)
            .ToListAsync(ct);

        return rows.Select(r => (r.Id, r.Score)).ToList();
    }

    private sealed class FullTextRow
    {
        public int Id { get; set; }
        public double Score { get; set; }
    }
}
