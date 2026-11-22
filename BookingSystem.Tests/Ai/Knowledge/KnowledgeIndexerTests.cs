using BookingSystem.Ai;
using BookingSystem.Ai.Knowledge;
using BookingSystem.Data;
using BookingSystem.Models;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BookingSystem.Tests.Ai.Knowledge;

public class KnowledgeIndexerTests : IDisposable
{
    private readonly ApplicationDbContext _db = TestDbContextFactory.Create();
    private readonly string _dir = Directory.CreateTempSubdirectory("kb-test-").FullName;
    private readonly FakeEmbeddingGenerator _generator = new(text => [text.Length, 1, 0]);

    public KnowledgeIndexerTests()
    {
        Write("deposit", "deposit", ("Що таке депозит", "Депозит блокується на картці."), ("Розмір депозиту", "Седан — 7 000 ₴."));
        Write("fuel", "fuel", ("Повний — повний", "Повертайте авто з повним баком."));
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private void Write(string name, string category, params (string Title, string Content)[] sections) =>
        File.WriteAllText(Path.Combine(_dir, name + ".md"),
            $"---\ncategory: {category}\n---\n# {name}\n\n" +
            string.Join("\n\n", sections.Select(s => $"## {s.Title}\n{s.Content}")));

    private KnowledgeIndexer CreateSut(bool withEmbeddings = true)
    {
        var store = new KnowledgeStore(async ct => await _db.KnowledgeChunks.AsNoTracking().ToListAsync(ct));
        var options = Options.Create(new AiOptions());
        return new KnowledgeIndexer(
            _db,
            new FakeEmbeddingFactory(withEmbeddings ? _generator : null),
            store,
            Mock.Of<IHostEnvironment>(e => e.ContentRootPath == _dir),
            options,
            new AgentClock(TimeProvider.System, options),
            NullLogger<KnowledgeIndexer>.Instance);
    }

    [Fact]
    public async Task FirstRun_IndexesAllDocumentsWithEmbeddingsAndSearchText()
    {
        var report = await CreateSut().IndexAsync(_dir);

        report.Should().BeEquivalentTo(new { Documents = 2, Indexed = 2, Skipped = 0, Chunks = 3, WithEmbeddings = true });
        var chunk = await _db.KnowledgeChunks.SingleAsync(c => c.DocumentId == "deposit" && c.ChunkIndex == 2);
        chunk.Key.Should().Be("deposit#2");
        chunk.Category.Should().Be("deposit");
        chunk.SearchText.Should().Contain("депозит").And.Contain("седан");
        KnowledgeStore.FromBytes(chunk.Embedding).Should().HaveCount(3);
        chunk.EmbeddingModel.Should().Be("test:embed");
    }

    [Fact]
    public async Task SecondRun_SkipsUnchangedFilesWithoutEmbeddingAgain()
    {
        await CreateSut().IndexAsync(_dir);
        var embedded = _generator.TextsEmbedded;

        var report = await CreateSut().IndexAsync(_dir);

        report.Skipped.Should().Be(2);
        report.Indexed.Should().Be(0);
        _generator.TextsEmbedded.Should().Be(embedded);
    }

    [Fact]
    public async Task ChangedFile_IsReindexedAndOthersAreKept()
    {
        await CreateSut().IndexAsync(_dir);
        Write("fuel", "fuel", ("Повний — повний", "Повертайте авто з повним баком."), ("Електромобілі", "Заряд не менше 30%."));

        var report = await CreateSut().IndexAsync(_dir);

        report.Indexed.Should().Be(1);
        report.Skipped.Should().Be(1);
        (await _db.KnowledgeChunks.CountAsync(c => c.DocumentId == "fuel")).Should().Be(2);
    }

    [Fact]
    public async Task DeletedFile_IsRemovedFromIndex()
    {
        await CreateSut().IndexAsync(_dir);
        File.Delete(Path.Combine(_dir, "fuel.md"));

        var report = await CreateSut().IndexAsync(_dir);

        report.Removed.Should().Be(1);
        (await _db.KnowledgeChunks.AnyAsync(c => c.DocumentId == "fuel")).Should().BeFalse();
    }

    [Fact]
    public async Task WithoutEmbeddingKey_IndexesTextOnly_AndAddsVectorsLater()
    {
        var textOnly = await CreateSut(withEmbeddings: false).IndexAsync(_dir);
        var withVectors = await CreateSut(withEmbeddings: true).IndexAsync(_dir);

        textOnly.WithEmbeddings.Should().BeFalse();
        withVectors.Indexed.Should().Be(2, "chunks without embeddings must be re-embedded once a key is available");
        withVectors.WithEmbeddings.Should().BeTrue();
    }

    [Fact]
    public async Task Force_ReindexesEverything()
    {
        await CreateSut().IndexAsync(_dir);

        var report = await CreateSut().IndexAsync(_dir, force: true);

        report.Indexed.Should().Be(2);
        (await _db.KnowledgeChunks.CountAsync()).Should().Be(3);
    }

    [Theory]
    [InlineData("Quota exceeded ... Please retry in 29.926368639s.", 30.926368639)]
    [InlineData("Too many requests", 31)]
    public void RetryDelay_FollowsProviderHint(string message, double expectedSeconds)
    {
        KnowledgeIndexer.RetryDelay(message).TotalSeconds.Should().BeApproximately(expectedSeconds, 1e-6);
    }
}
