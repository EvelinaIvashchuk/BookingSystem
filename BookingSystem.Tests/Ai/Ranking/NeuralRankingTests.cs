using System.Text.Json;
using BookingSystem.Ai;
using BookingSystem.Ai.Knowledge;
using BookingSystem.Ai.Ranking;
using BookingSystem.Ai.Tools;
using BookingSystem.Data;
using BookingSystem.Tests.Ai.Knowledge;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using static BookingSystem.Tests.Ai.AiTestData;

namespace BookingSystem.Tests.Ai.Ranking;

public class NeuralRankingTests : IAsyncLifetime
{
    private static readonly string Root = FindRoot();
    private static readonly string WebRoot = Path.Combine(Root, "BookingSystem");
    private static readonly string CarRankingDir = Path.Combine(WebRoot, "CarRanking");

    private readonly ApplicationDbContext _db = TestDbContextFactory.Create();

    public Task InitializeAsync() => SeedAsync(_db);

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BookingSystem.sln"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }

    private static float[] PseudoEmbedding(string text)
    {
        var rng = new Random(text.GetHashCode(StringComparison.Ordinal));
        return Enumerable.Range(0, 3072).Select(_ => (float)(rng.NextDouble() - 0.5)).ToArray();
    }

    private static CarRanker CreateRanker(AiOptions? options = null)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(WebRoot);
        var embeddings = new FakeEmbeddingFactory(new FakeEmbeddingGenerator(PseudoEmbedding), "gemini:gemini-embedding-001");
        return new CarRanker(Options.Create(options ?? new AiOptions()), embeddings, new QueryEmbeddingCache(), NullLogger<CarRanker>.Instance, env.Object);
    }

    // ── The C# networks give the same scores as the numpy reference that reads the exported files ──

    [Theory]
    [InlineData(RankingModelIds.Mlp, "car_ranker_mlp.json")]
    [InlineData(RankingModelIds.TwoTower, "car_ranker_two_tower.json")]
    public void ExportedNetwork_GivesTheSameScoresAsTheReference(string id, string file)
    {
        ICarScorer scorer = id == RankingModelIds.Mlp
            ? MlpScorer.Load(Path.Combine(CarRankingDir, file))
            : TwoTowerScorer.Load(Path.Combine(CarRankingDir, file));
        using var parity = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "BookingSystem.Tests", "TestData", "car_ranker_nn_parity.json")));
        var cars = parity.RootElement.GetProperty("cars").EnumerateArray()
            .Select(c => c.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? double.NaN : v.GetDouble()).ToArray()).ToList();
        var wishes = parity.RootElement.GetProperty("wishes").EnumerateArray().ToList();
        var expected = parity.RootElement.GetProperty("scores").GetProperty(id).EnumerateArray().ToList();

        scorer.Id.Should().Be(id);
        scorer.EmbeddingModel.Should().Be("gemini:gemini-embedding-001");
        wishes.Should().HaveCount(6);
        cars.Should().HaveCount(48);
        cars.Should().Contain(c => c.Any(double.IsNaN), "the parity data must exercise the missing-value path");

        for (var w = 0; w < wishes.Count; w++)
        {
            var embedding = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(
                Convert.FromBase64String(wishes[w].GetProperty("embedding").GetString()!)).ToArray();
            var cosines = wishes[w].GetProperty("cosines").EnumerateArray().Select(v => v.GetDouble()).ToList();

            var scores = scorer.Score(embedding, cosines, cars);

            scores.Should().HaveCount(48);
            var want = expected[w].EnumerateArray().Select(v => v.GetDouble()).ToArray();
            for (var c = 0; c < 48; c++)
                scores[c].Should().BeApproximately(want[c], 1e-6, "wish {0}, car {1}", w, c);
        }
    }

    [Fact]
    public void LightGbmScorer_MatchesTheModelItWraps()
    {
        var model = CarRankingModel.Load(Path.Combine(CarRankingDir, "car_ranker.json"));
        var scorer = new LightGbmScorer(model);
        var wish = PseudoEmbedding("wish");
        var cars = (await_cars()).ToList();
        var cosines = cars.Select((_, i) => 0.1 * i).ToList();

        var scores = scorer.Score(wish, cosines, cars);

        var pcs = model.Project(wish);
        for (var i = 0; i < cars.Count; i++)
            scores[i].Should().BeApproximately(model.Score(CarRanker.Features(model, cosines[i], pcs, cars[i])), 1e-12);

        static IEnumerable<double[]> await_cars() =>
            Enumerable.Range(0, 5).Select(i => Enumerable.Range(0, CarRankingFeatures.CarFeatureNames.Count).Select(j => (double)(i + j)).ToArray());
    }

    // ── CarRanker with a chosen model ───────────────────────────────────────

    [Fact]
    public void AvailableModels_ListsAllThreeFilesWithTheirTestQuality()
    {
        var models = CreateRanker().AvailableModels();

        models.Select(m => m.Id).Should().Equal(RankingModelIds.LightGbm, RankingModelIds.Mlp, RankingModelIds.TwoTower);
        models[0].TestNdcg5.Should().BeApproximately(0.895, 0.001);
        models[1].TestNdcg5.Should().BeApproximately(0.945, 0.001);
        models[2].TestNdcg5.Should().BeApproximately(0.962, 0.001);
    }

    [Fact]
    public void AvailableModels_SkipsMissingFilesAndIsEmptyWhenRankingIsOff()
    {
        CreateRanker(new AiOptions { CarRanking = { MlpModelPath = "CarRanking/missing.json" } }).AvailableModels()
            .Select(m => m.Id).Should().Equal(RankingModelIds.LightGbm, RankingModelIds.TwoTower);
        CreateRanker(new AiOptions { CarRanking = { Enabled = false } }).AvailableModels().Should().BeEmpty();
    }

    [Theory]
    [InlineData(RankingModelIds.LightGbm)]
    [InlineData(RankingModelIds.Mlp)]
    [InlineData(RankingModelIds.TwoTower)]
    public async Task RankAsync_WithEachModel_RanksEveryCar(string modelId)
    {
        var ranker = CreateRanker();
        var cars = (await CreateUnitOfWork(_db).Cars.GetAllWithCategoryAsync()).ToList();

        var ranked = await ranker.RankAsync("нас семеро і багато валіз", cars, modelId: modelId);

        ranked.Should().NotBeNull();
        ranked!.Select(r => r.Car.Id).Should().BeEquivalentTo(cars.Select(c => c.Id));
        ranked.Select(r => r.Score).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task RankAsync_ModelsOrderCarsDifferently_AndUnknownIdFallsBackToLightGbm()
    {
        var ranker = CreateRanker();
        var cars = (await CreateUnitOfWork(_db).Cars.GetAllWithCategoryAsync()).ToList();
        const string wish = "економне для міста";

        var lgbm = (await ranker.RankAsync(wish, cars, modelId: RankingModelIds.LightGbm))!.Select(r => r.Score).ToList();
        var twoTower = (await ranker.RankAsync(wish, cars, modelId: RankingModelIds.TwoTower))!.Select(r => r.Score).ToList();
        var unknown = (await ranker.RankAsync(wish, cars, modelId: "no-such-model"))!.Select(r => r.Score).ToList();
        var defaulted = (await ranker.RankAsync(wish, cars))!.Select(r => r.Score).ToList();

        twoTower.Should().NotEqual(lgbm);
        unknown.Should().Equal(lgbm);
        defaulted.Should().Equal(lgbm);
    }

    [Fact]
    public async Task RankAsync_WhenTheChosenModelFileIsMissing_FallsBackToLightGbm()
    {
        var ranker = CreateRanker(new AiOptions { CarRanking = { MlpModelPath = "CarRanking/missing.json" } });
        var cars = (await CreateUnitOfWork(_db).Cars.GetAllWithCategoryAsync()).ToList();

        var viaMlp = await ranker.RankAsync("для міста", cars, modelId: RankingModelIds.Mlp);
        var viaLgbm = await ranker.RankAsync("для міста", cars, modelId: RankingModelIds.LightGbm);

        viaMlp!.Select(r => r.Score).Should().Equal(viaLgbm!.Select(r => r.Score));
    }

    // ── Administrator's choice ──────────────────────────────────────────────

    private RankingModelSettings CreateSettings(ApplicationDbContext? db = null, AiOptions? options = null) =>
        new(db ?? _db, CreateRanker(options), Options.Create(options ?? new AiOptions()), TimeProvider.System);

    [Fact]
    public async Task Settings_DefaultIsLightGbm_AndChoiceIsStoredAndShared()
    {
        var db = TestDbContextFactory.Create();
        var sut = CreateSettings(db);
        (await sut.GetActiveModelAsync()).Should().Be(RankingModelIds.LightGbm);
        sut.GetAvailableModels().Select(m => m.Id).Should().Equal(RankingModelIds.LightGbm, RankingModelIds.Mlp, RankingModelIds.TwoTower);

        await sut.SetActiveModelAsync(RankingModelIds.TwoTower);

        (await CreateSettings(db).GetActiveModelAsync()).Should().Be(RankingModelIds.TwoTower);
        db.AppSettings.Count().Should().Be(1);
        await sut.SetActiveModelAsync(RankingModelIds.Mlp);
        db.AppSettings.Count().Should().Be(1);
        (await sut.GetActiveModelAsync()).Should().Be(RankingModelIds.Mlp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gpt-5")]
    [InlineData("LIGHTGBM")]
    public async Task Settings_RejectUnknownModels(string id)
    {
        var sut = CreateSettings();

        var act = () => sut.SetActiveModelAsync(id);

        await act.Should().ThrowAsync<ArgumentException>();
        (await sut.GetActiveModelAsync()).Should().Be(RankingModelIds.LightGbm);
    }

    [Fact]
    public async Task Settings_FallBackToDefault_WhenTheChosenModelFileDisappears()
    {
        var db = TestDbContextFactory.Create();
        await CreateSettings(db).SetActiveModelAsync(RankingModelIds.Mlp);

        var withoutMlp = new AiOptions { CarRanking = { MlpModelPath = "CarRanking/missing.json" } };

        (await CreateSettings(db, withoutMlp).GetActiveModelAsync()).Should().Be(RankingModelIds.LightGbm);
    }

    // ── Search tool uses the chosen model ───────────────────────────────────

    [Fact]
    public async Task Search_UsesTheRankingModelChosenByAdministrator()
    {
        var ranker = CreateRanker();
        var settings = new Mock<IRankingModelSettings>();
        settings.Setup(s => s.GetActiveModelAsync(It.IsAny<CancellationToken>())).ReturnsAsync(RankingModelIds.TwoTower);
        var tools = new BookingTools(CreateUnitOfWork(_db), CreateCarService(_db), Mock.Of<IKnowledgeSearch>(), CreateClock(), new AiOptions(),
            new AgentTurnContext(Guid.NewGuid(), null, Today), ranker, settings.Object);

        var result = (CarSearchResult)await tools.SearchAvailableCarsAsync(Pickup, Return, wishes: "нас семеро і багато валіз");

        // only the cars that are free for the dates are ranked
        var availableIds = ((CarSearchResult)await new BookingTools(CreateUnitOfWork(_db), CreateCarService(_db), Mock.Of<IKnowledgeSearch>(), CreateClock(),
            new AiOptions(), new AgentTurnContext(Guid.NewGuid(), null, Today)).SearchAvailableCarsAsync(Pickup, Return)).Cars.Select(c => c.Id).ToHashSet();
        var cars = (await CreateUnitOfWork(_db).Cars.GetAllWithCategoryAsync()).Where(c => availableIds.Contains(c.Id) || result.Cars.Any(o => o.Id == c.Id)).ToList();
        var expected = await ranker.RankAsync("нас семеро і багато валіз", cars, modelId: RankingModelIds.TwoTower);
        var lgbm = await ranker.RankAsync("нас семеро і багато валіз", cars, modelId: RankingModelIds.LightGbm);
        settings.Verify(s => s.GetActiveModelAsync(It.IsAny<CancellationToken>()), Times.Once);
        result.Cars[0].Id.Should().Be(expected![0].Car.Id);
        result.Cars.Select(c => c.Id).Should().Equal(expected.Take(result.Cars.Count).Select(r => r.Car.Id));
        expected.Select(r => r.Car.Id).Should().NotEqual(lgbm!.Select(r => r.Car.Id), "the two models order cars differently");
    }
}
