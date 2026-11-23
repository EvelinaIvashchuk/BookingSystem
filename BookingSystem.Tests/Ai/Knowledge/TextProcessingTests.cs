using BookingSystem.Ai.Knowledge;
using FluentAssertions;

namespace BookingSystem.Tests.Ai.Knowledge;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("депозит", "депозиту", "депозитом", "депозити")]
    [InlineData("франшиза", "франшизи", "франшизу", "франшизою")]
    [InlineData("посвідчення", "посвідченням", "посвідчень", "посвідченні")]
    [InlineData("пробіг", "пробігу", "пробігом", "пробіги")]
    public void Stem_MapsInflectedFormsToOneStem(params string[] forms)
    {
        forms.Select(TextNormalizer.Stem).Distinct().Should().ContainSingle();
    }

    [Fact]
    public void Tokenize_LowercasesAndRemovesApostrophes()
    {
        TextNormalizer.Tokenize("П'ятниця, ДТП та пʼять днів!")
            .Should().Equal("пятниця", "дтп", "та", "пять", "днів");
    }

    [Fact]
    public void ContentStems_DropStopWords()
    {
        TextNormalizer.ContentStems("Який у вас депозит для SUV?")
            .Should().Equal("депозит", "suv");
    }

    [Fact]
    public void ShortWordsAndNumbersAreKeptAsIs()
    {
        TextNormalizer.Stem("вік").Should().Be("вік");
        TextNormalizer.Stem("5000").Should().Be("5000");
    }

    [Fact]
    public void StemsMatch_IsPrefixBased()
    {
        TextNormalizer.StemsMatch("страхуванн", "страх").Should().BeTrue();
        TextNormalizer.StemsMatch("кор", "кордон").Should().BeFalse("too short to be a reliable prefix");
    }
}

public class MarkdownChunkerTests
{
    private const string Sample = """
        ---
        category: deposit
        title: Депозит і оплата
        ---
        # Депозит і оплата

        ## Що таке депозит
        Депозит — це сума, яку ми блокуємо.

        ## Розмір депозиту
        Хетчбек — 5 000 ₴.
        Седан — 7 000 ₴.
        """;

    [Fact]
    public void Parse_ReadsFrontmatterAndSplitsBySections()
    {
        var doc = MarkdownChunker.Parse("deposit", Sample);

        doc.Category.Should().Be("deposit");
        doc.Title.Should().Be("Депозит і оплата");
        doc.Chunks.Select(c => (c.Index, c.Title)).Should().Equal((1, "Що таке депозит"), (2, "Розмір депозиту"));
        doc.Chunks[1].Content.Should().Contain("Седан — 7 000 ₴.");
    }

    [Fact]
    public void Parse_LongSectionIsSplitByParagraphsWithOneSentenceOverlap()
    {
        var paragraph = string.Join(" ", Enumerable.Repeat("Слово слово слово слово слово.", 12)); // ~120 tokens
        var markdown = $"## Довгий розділ\n{paragraph} Перше закінчення.\n\n{paragraph} Друге закінчення.\n\n{paragraph}";

        var doc = MarkdownChunker.Parse("long", markdown, maxTokens: 150);

        doc.Chunks.Should().HaveCount(3);
        doc.Chunks.Should().OnlyContain(c => c.Title == "Довгий розділ");
        doc.Chunks[1].Content.Should().StartWith("Перше закінчення.");
        doc.Chunks[2].Content.Should().StartWith("Друге закінчення.");
    }

    [Fact]
    public void Parse_WithoutFrontmatter_FallsBackToDocumentId()
    {
        var doc = MarkdownChunker.Parse("misc", "## Розділ\nТекст.");

        doc.Category.Should().Be("misc");
        doc.Chunks.Should().ContainSingle();
    }
}

/// <summary>Integrity checks of the real knowledge base shipped with the application.</summary>
public class KnowledgeBaseFilesTests
{
    private static readonly string[] Categories =
    [
        "documents", "deposit", "insurance", "cancellation", "fuel", "mileage",
        "late-return", "damage", "borders", "extras", "pickup", "booking",
        "children", "ev", "winter", "roadside", "fines", "long-term",
        "support", "rules", "cars", "technical", "privacy", "airport"
    ];

    internal static string KnowledgeBaseDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BookingSystem.sln")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "BookingSystem", "KnowledgeBase");
    }

    private static List<MarkdownDocument> LoadAll() =>
        Directory.GetFiles(KnowledgeBaseDirectory(), "*.md")
            .Select(f => MarkdownChunker.Parse(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f)))
            .ToList();

    [Fact]
    public void EveryCategoryHasExactlyOneDocument()
    {
        LoadAll().Select(d => d.Category).Should().BeEquivalentTo(Categories);
    }

    [Fact]
    public void KnowledgeBaseSizeIsWithinPlannedRange()
    {
        var chunks = LoadAll().SelectMany(d => d.Chunks).ToList();

        chunks.Count.Should().BeInRange(150, 300);
        chunks.Should().OnlyContain(c => c.TokenCount <= 350 && c.Content.Length > 40);
    }

    [Fact]
    public void ChunkTitlesAreUniqueWithinDocument()
    {
        foreach (var doc in LoadAll())
            doc.Chunks.Select(c => c.Title).Should().OnlyHaveUniqueItems(doc.DocumentId);
    }
}

public class QueryFeaturesTests
{
    private static readonly DomainVocabulary Vocabulary = new(["Розмір депозиту за категоріями", "Безлімітний пробіг"]);

    [Fact]
    public void ShortTermQuery_IsAllDomainTerms()
    {
        var f = QueryFeatures.Extract("депозит", Vocabulary);

        f.WordCount.Should().Be(1);
        f.DomainTermShare.Should().Be(1);
        f.HasNumber.Should().BeFalse();
        f.PredictedCategory.Should().Be("deposit");
    }

    [Fact]
    public void DescriptiveQuery_HasManyWordsAndFewTerms()
    {
        var f = QueryFeatures.Extract("що буде, якщо я поверну машину на день пізніше, ніж домовлялися", Vocabulary);

        f.WordCount.Should().BeGreaterThan(4);
        f.DomainTermShare.Should().BeLessThan(0.5);
        f.PredictedCategory.Should().Be("late-return");
    }

    [Theory]
    [InlineData("скільки коштує крісло на 5 днів", true)]
    [InlineData("депозит 10000 грн", true)]
    [InlineData("ціна в гривнях за км", true)]
    [InlineData("чи потрібна зелена карта", false)]
    public void HasNumber_DetectsNumbersAndAmounts(string query, bool expected)
    {
        QueryFeatures.Extract(query, Vocabulary).HasNumber.Should().Be(expected);
    }

    [Fact]
    public void AmbiguousQuery_HasNoPredictedCategory()
    {
        // one keyword of "deposit" and one of "insurance" → tie
        QueryFeatures.Extract("депозит і франшиза", Vocabulary).PredictedCategory.Should().BeNull();
    }

    [Fact]
    public void Weights_FavourFullTextForShortTermQueriesAndVectorsForLongOnes()
    {
        var options = new BookingSystem.Ai.AdaptiveFusionOptions();
        var shortQuery = QueryFeatures.Extract("франшиза каско", Vocabulary).Weights(options);
        var longQuery = QueryFeatures.Extract("що мені робити якщо я подряпав машину на парковці біля торгового центру", Vocabulary).Weights(options);

        shortQuery.FullText.Should().BeGreaterThan(0.5);
        longQuery.FullText.Should().BeLessThan(0.5);
        (shortQuery.FullText + shortQuery.Vector).Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void Weights_AreClamped()
    {
        var options = new BookingSystem.Ai.AdaptiveFusionOptions { A = 5, MinWeight = 0.15, MaxWeight = 0.85 };

        QueryFeatures.Extract("депозит", Vocabulary).Weights(options).FullText.Should().Be(0.85);
    }
}

public class HybridFusionTests
{
    [Fact]
    public void Rrf_SumsReciprocalRanks()
    {
        // doc 1: vec rank 1, ft rank 2; doc 2: vec rank 2, ft rank 1; doc 3: only ft rank 3
        var fused = HybridFusion.Fuse([1, 2], [2, 1, 3], k: 60);

        fused.Single(x => x.Id == 1).Score.Should().BeApproximately(1.0 / 61 + 1.0 / 62, 1e-12);
        fused.Single(x => x.Id == 3).Score.Should().BeApproximately(1.0 / 63, 1e-12);
        fused.Single(x => x.Id == 3).VectorRank.Should().BeNull();
        fused.Last().Id.Should().Be(3);
    }

    [Fact]
    public void Weights_ShiftTheOrder()
    {
        // doc 10 is first by vectors, doc 20 first by full text
        var vectorHeavy = HybridFusion.Fuse([10, 20], [20, 10], 60, vectorWeight: 0.85, fullTextWeight: 0.15);
        var textHeavy = HybridFusion.Fuse([10, 20], [20, 10], 60, vectorWeight: 0.15, fullTextWeight: 0.85);

        vectorHeavy[0].Id.Should().Be(10);
        textHeavy[0].Id.Should().Be(20);
    }

    [Fact]
    public void CategoryBoost_PromotesMatchingDocumentWithoutRemovingOthers()
    {
        var fused = HybridFusion.Fuse([1, 2, 3], [1, 2, 3], 60, boost: id => id == 3 ? 1.25 : 1);

        fused[0].Id.Should().Be(3);
        fused.Should().HaveCount(3);
    }

    [Fact]
    public void EqualScores_AreOrderedByBestRank()
    {
        // doc 5 only in vector list at rank 1, doc 6 only in full-text list at rank 1 → equal score
        var fused = HybridFusion.Fuse([5], [6], 60);

        fused.Select(x => x.Id).Should().Equal(5, 6);
    }
}
