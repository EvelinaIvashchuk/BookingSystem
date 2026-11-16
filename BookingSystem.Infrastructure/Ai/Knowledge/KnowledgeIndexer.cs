using System.ClientModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BookingSystem.Data;
using BookingSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BookingSystem.Ai.Knowledge;

public sealed record KnowledgeIndexReport(
    int Documents, int Indexed, int Skipped, int Removed, int Chunks, string? EmbeddingModel, bool WithEmbeddings);

/// <summary>
/// markdown → chunks → embeddings → <c>KnowledgeChunks</c>.
/// Idempotent: a document is re-indexed only when its file hash or the embedding model changed
/// (or embeddings are missing and a generator is now available).
/// </summary>
public sealed class KnowledgeIndexer(
    ApplicationDbContext db,
    IEmbeddingGeneratorFactory embeddingFactory,
    KnowledgeStore store,
    IHostEnvironment environment,
    IOptions<AiOptions> options,
    AgentClock clock,
    ILogger<KnowledgeIndexer> logger)
{
    private readonly KnowledgeBaseOptions _kb = options.Value.KnowledgeBase;

    public string DefaultDirectory => Path.Combine(environment.ContentRootPath, _kb.Path);

    public async Task<KnowledgeIndexReport> IndexAsync(string? directory = null, bool force = false, CancellationToken ct = default)
    {
        directory ??= DefaultDirectory;
        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Knowledge base folder {Directory} not found; nothing to index", directory);
            return new KnowledgeIndexReport(0, 0, 0, 0, 0, null, false);
        }

        var generator = TryCreateGenerator();
        var modelId = generator is null ? null : embeddingFactory.ModelId;

        var existing = (await db.KnowledgeChunks.ToListAsync(ct))
            .GroupBy(c => c.DocumentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var files = Directory.GetFiles(directory, "*.md").OrderBy(f => f, StringComparer.Ordinal).ToList();
        int indexed = 0, skipped = 0, removed = 0;

        foreach (var file in files)
        {
            var documentId = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var text = await File.ReadAllTextAsync(file, Encoding.UTF8, ct);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            var old = existing.GetValueOrDefault(documentId) ?? [];

            if (!force && IsUpToDate(old, hash, modelId))
            {
                skipped++;
                continue;
            }

            var document = MarkdownChunker.Parse(documentId, text, _kb.MaxChunkTokens);
            var vectors = generator is null
                ? null
                : await EmbedAsync(generator, document.Chunks.Select(c => $"{c.Title}\n{c.Content}").ToList(), ct);

            db.KnowledgeChunks.RemoveRange(old);
            await db.SaveChangesAsync(ct);   // free the unique (DocumentId, ChunkIndex) keys first

            var now = clock.UtcNow;
            db.KnowledgeChunks.AddRange(document.Chunks.Select((c, i) => new KnowledgeChunk
            {
                DocumentId = documentId,
                ChunkIndex = c.Index,
                Category = document.Category,
                Title = c.Title,
                Content = c.Content,
                SearchText = TextNormalizer.ToSearchText($"{c.Title}\n{c.Content}"),
                Embedding = vectors is null ? null : KnowledgeStore.ToBytes(vectors[i]),
                EmbeddingModel = vectors is null ? null : modelId,
                TokenCount = c.TokenCount,
                SourceHash = hash,
                IndexedAt = now
            }));
            await db.SaveChangesAsync(ct);

            indexed++;
            logger.LogInformation("Indexed {Document}: {Chunks} chunks, embeddings: {Model}",
                documentId, document.Chunks.Count, modelId ?? "none");
        }

        var fileIds = files.Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant()).ToHashSet();
        foreach (var (documentId, chunks) in existing.Where(e => !fileIds.Contains(e.Key)))
        {
            db.KnowledgeChunks.RemoveRange(chunks);
            removed++;
            logger.LogInformation("Removed knowledge document {Document}: file no longer exists", documentId);
        }
        await db.SaveChangesAsync(ct);

        store.Invalidate();

        var total = await db.KnowledgeChunks.CountAsync(ct);
        var withEmbeddings = total > 0 && await db.KnowledgeChunks.AllAsync(c => c.Embedding != null, ct);
        return new KnowledgeIndexReport(files.Count, indexed, skipped, removed, total, modelId, withEmbeddings);
    }

    private static bool IsUpToDate(List<KnowledgeChunk> old, string hash, string? modelId) =>
        old.Count > 0
        && old.All(c => c.SourceHash == hash)
        // Without a generator keep whatever embeddings exist; with one, they must be complete and current.
        && (modelId is null || old.All(c => c.Embedding is not null && c.EmbeddingModel == modelId));

    private IEmbeddingGenerator<string, Embedding<float>>? TryCreateGenerator()
    {
        try
        {
            return embeddingFactory.Create();
        }
        catch (AiNotConfiguredException ex)
        {
            logger.LogWarning("Indexing without embeddings (vector search unavailable): {Reason}", ex.Message);
            return null;
        }
    }

    private const int MaxRateLimitRetries = 6;

    private async Task<List<float[]>> EmbedAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator, IReadOnlyList<string> texts, CancellationToken ct)
    {
        var result = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(Math.Max(1, _kb.EmbeddingBatchSize)))
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var embeddings = await generator.GenerateAsync(batch, cancellationToken: ct);
                    result.AddRange(embeddings.Select(e => e.Vector.ToArray()));
                    break;
                }
                catch (ClientResultException ex) when (ex.Status == 429 && attempt <= MaxRateLimitRetries)
                {
                    // Free tiers limit embedded texts per minute (Gemini: 100); wait as the provider asks.
                    var delay = RetryDelay(ex.Message);
                    logger.LogInformation(
                        "Embedding rate limit reached; waiting {Delay:0} s (attempt {Attempt}/{Max})",
                        delay.TotalSeconds, attempt, MaxRateLimitRetries);
                    await Task.Delay(delay, ct);
                }
            }
        }
        return result;
    }

    /// <summary>Parses "retry in 29.9s" from the provider message; 30 s if absent.</summary>
    internal static TimeSpan RetryDelay(string message)
    {
        var match = Regex.Match(message, @"retry in ([\d.]+)\s*s", RegexOptions.IgnoreCase);
        var seconds = match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
            ? s
            : 30;
        return TimeSpan.FromSeconds(Math.Clamp(seconds + 1, 1, 120));
    }
}

/// <summary>Indexes changed knowledge-base files in the background after startup.</summary>
public sealed class KnowledgeIndexingHostedService(
    IServiceScopeFactory scopes,
    IOptions<AiOptions> options,
    ILogger<KnowledgeIndexingHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.KnowledgeBase.IndexOnStartup) return;

        await Task.Yield();   // do not delay application startup
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var indexer = scope.ServiceProvider.GetRequiredService<KnowledgeIndexer>();
            var report = await indexer.IndexAsync(ct: stoppingToken);

            logger.LogInformation(
                "Knowledge base: {Documents} documents ({Indexed} indexed, {Skipped} unchanged, {Removed} removed), {Chunks} chunks, embeddings {Model}",
                report.Documents, report.Indexed, report.Skipped, report.Removed, report.Chunks,
                report.WithEmbeddings ? report.EmbeddingModel : "missing");
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Knowledge base indexing failed; search will use what is already indexed");
        }
    }
}
