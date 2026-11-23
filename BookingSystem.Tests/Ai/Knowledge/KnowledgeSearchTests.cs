using BookingSystem.Ai;
using BookingSystem.Ai.Knowledge;
using BookingSystem.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookingSystem.Tests.Ai.Knowledge;

public class KnowledgeSearchTests
{
    // Vectors: 1 and 4 are about the deposit, 2 insurance, 3 late return.
    private static readonly KnowledgeChunk[] Chunks =
    [
        Chunk(1, "deposit", 1, "deposit", "Розмір депозиту", [1, 0, 0]),
        Chunk(2, "insurance", 1, "insurance", "Франшиза", [0, 1, 0]),
        Chunk(3, "late-return", 1, "late-return", "Запізнення", [0, 0, 1]),
        Chunk(4, "deposit", 2, "deposit", "Депозит готівкою", [0.7f, 0.7f, 0])
    ];

    private readonly AiOptions _options = new();
    private FakeEmbeddingGenerator _generator = new(_ => [0, 0, 1]);
    private Dictionary<string, int[]> _fullText = [];

    private FakeFullTextIndex? _index;

    private KnowledgeSearch CreateSut(bool embeddingsAvailable = true)
    {
        var store = new KnowledgeStore(_ => Task.FromResult<IReadOnlyList<KnowledgeChunk>>(Chunks));
        return new KnowledgeSearch(
            store,
            _index = new FakeFullTextIndex(_fullText),
            new FakeEmbeddingFactory(embeddingsAvailable ? _generator : null),
            new QueryEmbeddingCache(),
            Options.Create(_options),
            NullLogger<KnowledgeSearch>.Instance);
    }

    private static KnowledgeChunk Chunk(int id, string doc, int index, string category, string title, float[] vector) => new()
    {
        Id = id, DocumentId = doc, ChunkIndex = index, Category = category, Title = title,
        Content = title + " — текст", Embedding = KnowledgeStore.ToBytes(vector), EmbeddingModel = "test:embed"
    };

    [Fact]
    public async Task Vector_RanksByCosineSimilarity()
    {
        _generator = new FakeEmbeddingGenerator(_ => [1, 0.1f, 0]);

        var result = await CreateSut().SearchAsync("скільки блокують на картці", 3, KnowledgeSearchMode.Vector);

        result.Hits.Select(h => h.Key).Should().Equal("deposit#1", "deposit#2", "insurance#1");
        result.Hits[0].VectorRank.Should().Be(1);
        result.Hits[0].FullTextRank.Should().BeNull();
    }

    [Fact]
    public async Task FullText_UsesNormalizedQueryAndDoesNotEmbed()
    {
        _fullText["франшиз"] = [2, 4];

        var result = await CreateSut().SearchAsync("Франшиза?", 3, KnowledgeSearchMode.FullText);

        result.Hits.Select(h => h.Key).Should().Equal("insurance#1", "deposit#2");
        _generator.Calls.Should().Be(0);
    }

    /// <summary>
    /// Short term query "депозит": vectors put the late-return chunk first (noise) and full text also
    /// matches it weakly. Vector order 3,1,4,2; full-text order 4,3,1 (k = 60):
    /// RRF:      #3 = 1/61+1/62 = .03252, #4 = 1/63+1/61 = .03227, #1 = 1/62+1/63 = .03200 → top-2 {3, 4};
    /// adaptive: w_ft = .85, w_vec = .15, ×1.25 for "deposit":
    ///           #4 = (.15/63+.85/61)·1.25 = .02039, #1 = (.15/62+.85/63)·1.25 = .01989, #3 = .15/61+.85/62 = .01617 → top-2 {4, 1}.
    /// </summary>
    [Fact]
    public async Task Adaptive_PrefersFullTextAndCategoryForShortTermQueries_WhereRrfDoesNot()
    {
        _generator = new FakeEmbeddingGenerator(_ => [0.05f, 0, 1]);   // vector order: 3, 1, 4, 2
        _fullText["депозит"] = [4, 3, 1];
        var sut = CreateSut();

        var rrf = await sut.SearchAsync("депозит", 2, KnowledgeSearchMode.HybridRrf);
        var adaptive = await sut.SearchAsync("депозит", 2, KnowledgeSearchMode.HybridAdaptive);

        rrf.Hits.Select(h => h.Key).Should().Contain("late-return#1");
        adaptive.Hits.Select(h => h.Key).Should().BeEquivalentTo("deposit#1", "deposit#2");
        adaptive.Features!.PredictedCategory.Should().Be("deposit");
        adaptive.Features.FullTextWeight.Should().Be(0.85);
    }

    [Fact]
    public async Task Rrf_ScoreMatchesFormula()
    {
        _generator = new FakeEmbeddingGenerator(_ => [1, 0, 0]);       // vector order: 1, 4, 2/3
        _fullText["депозит"] = [4, 1];

        var result = await CreateSut().SearchAsync("депозит", 1, KnowledgeSearchMode.HybridRrf);

        var top = result.Hits.Single();
        top.Key.Should().Be("deposit#1");
        top.Score.Should().BeApproximately(1.0 / 61 + 1.0 / 62, 1e-12);
    }

    [Fact]
    public async Task WithoutEmbeddings_HybridFallsBackToFullTextAndSaysSo()
    {
        _fullText["франшиз"] = [2];

        var result = await CreateSut(embeddingsAvailable: false).SearchAsync("франшиза", 3, KnowledgeSearchMode.HybridAdaptive);

        result.VectorUnavailable.Should().BeTrue();
        result.Hits.Select(h => h.Key).Should().Equal("insurance#1");
    }

    [Fact]
    public async Task QueryEmbeddingIsCachedAcrossModes()
    {
        var sut = CreateSut();

        await sut.SearchAsync("запізнення на годину", 3, KnowledgeSearchMode.Vector);
        await sut.SearchAsync("запізнення на годину", 3, KnowledgeSearchMode.HybridRrf);
        await sut.SearchAsync("запізнення на годину", 3, KnowledgeSearchMode.HybridAdaptive);

        _generator.Calls.Should().Be(1);
    }

    [Fact]
    public async Task DefaultModeComesFromConfiguration()
    {
        _options.Search.Mode = KnowledgeSearchMode.FullText;

        var result = await CreateSut().SearchAsync("депозит", 3);

        result.Mode.Should().Be(KnowledgeSearchMode.FullText);
    }

    [Fact]
    public async Task HybridAdaptive_ExpandsTheLexicalQueryFromTopVectorHits()
    {
        _generator = new FakeEmbeddingGenerator(_ => [1, 0.1f, 0]);

        await CreateSut().SearchAsync("скільки заблокують", 3, KnowledgeSearchMode.HybridAdaptive);

        _index!.Feedback.Should().ContainSingle().Which.Take(2).Should().Equal(1, 4);
    }

    [Theory]
    [InlineData(KnowledgeSearchMode.HybridRrf, true, true)]
    [InlineData(KnowledgeSearchMode.FullText, true, true)]
    [InlineData(KnowledgeSearchMode.HybridAdaptive, false, true)]
    [InlineData(KnowledgeSearchMode.HybridAdaptive, true, false)]
    public async Task QueryExpansion_OnlyInHybridAdaptiveWithVectorsWhenEnabled(KnowledgeSearchMode mode, bool enabled, bool embeddings)
    {
        _options.Search.QueryExpansion.Enabled = enabled;

        await CreateSut(embeddings).SearchAsync("депозит", 3, mode);

        _index!.Feedback.Should().BeEmpty();
        _index.Queries.Should().ContainSingle();
    }
}
