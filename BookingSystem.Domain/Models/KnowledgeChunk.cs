namespace BookingSystem.Models;

/// <summary>
/// A fragment of the rental-policy knowledge base (markdown files split by "##" headings),
/// with its embedding for vector search and normalized text for FULLTEXT search.
/// </summary>
public class KnowledgeChunk
{
    public int Id { get; set; }

    /// <summary>Source document (markdown file name without extension), e.g. <c>deposit</c>.</summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>1-based position of the chunk in the document; with <see cref="DocumentId"/> forms the key <c>deposit#2</c>.</summary>
    public int ChunkIndex { get; set; }

    /// <summary>Category from the document frontmatter (deposit, insurance, ...).</summary>
    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    /// <summary>Lower-cased, stemmed title + content; the FULLTEXT index is built on this column.</summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>float32 vector as bytes; <c>null</c> until embeddings are available.</summary>
    public byte[]? Embedding { get; set; }

    public string? EmbeddingModel { get; set; }

    /// <summary>Approximate number of tokens in the chunk.</summary>
    public int TokenCount { get; set; }

    /// <summary>SHA-256 of the source file; unchanged files are not re-indexed.</summary>
    public string SourceHash { get; set; } = string.Empty;

    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;

    public string Key => $"{DocumentId}#{ChunkIndex}";
}
