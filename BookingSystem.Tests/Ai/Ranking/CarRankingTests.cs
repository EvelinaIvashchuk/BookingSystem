using System.Text.Json;
using BookingSystem.Ai;
using BookingSystem.Ai.Knowledge;
using BookingSystem.Ai.Ranking;
using BookingSystem.Ai.Tools;
using BookingSystem.Data;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Tests.Ai.Knowledge;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using static BookingSystem.Tests.Ai.AiTestData;

namespace BookingSystem.Tests.Ai.Ranking;

public class CarRankingTests : IAsyncLifetime
{
    private static readonly string Root = FindRoot();
    private static readonly string WebRoot = Path.Combine(Root, "BookingSystem");
    private static readonly string ModelPath = Path.Combine(WebRoot, "CarRanking", "car_ranker.json");
    private static readonly string SpecsPath = Path.Combine(WebRoot, "CarRanking", "car_specs.json");

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

    // ── Exported model = the model trained in Python ────────────────────────

    [Fact]
    public void ExportedModel_GivesTheSameScoresAsLightGbm()
    {
        var model = CarRankingModel.Load(ModelPath);
        using var parity = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "BookingSystem.Tests", "TestData", "car_ranker_parity.json")));
        var vectors = parity.RootElement.GetProperty("vectors").EnumerateArray().ToList();
        var scores = parity.RootElement.GetProperty("scores").EnumerateArray().Select(s => s.GetDouble()).ToList();

        vectors.Should().HaveCount(300);
        for (var i = 0; i < vectors.Count; i++)
        {
            var x = vectors[i].EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? double.NaN : v.GetDouble()).ToArray();
            x.Should().HaveCount(model.FeatureNames.Count);
            model.Score(x).Should().BeApproximately(scores[i], 1e-9, "vector {0}", i);
        }
    }

    [Fact]
    public void ExportedModel_ProjectsWishEmbeddingLikeNumpy()
    {
        var model = CarRankingModel.Load(ModelPath);
        using var parity = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "BookingSystem.Tests", "TestData", "car_ranker_parity.json")));
        var expected = parity.RootElement.GetProperty("pcaProbe").GetProperty("projection").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var probe = Enumerable.Range(0, model.PcaMean.Length).Select(i => (float)Math.Sin(i * 0.37)).ToArray();

        model.Project(probe).Should().Equal(expected, (a, b) => Math.Abs(a - b) < 1e-6);
        model.FeatureNames.Should().StartWith("cos_wish_car").And.Contain(CarRankingFeatures.CarFeatureNames);
        model.EmbeddingModel.Should().Be("gemini:gemini-embedding-001");
    }

    [Theory]
    [InlineData(double.NaN, 10.0)]   // missing → default (left) branch
    [InlineData(1.0, 10.0)]          // 1 <= 2 → left
    [InlineData(5.0, 20.0)]          // 5 > 2 → right
    public void Tree_FollowsLightGbmMissingValueRule(double x, double expected)
    {
        var tree = new CarRankingModel.Tree(
        [
            new(false, 0, 2.0, true, "nan", 1, 2, 0),
            new(true, 0, 0, false, null, 0, 0, 10.0),
            new(true, 0, 0, false, null, 0, 0, 20.0)
        ]);

        tree.Predict([x]).Should().Be(expected);
    }

    [Fact]
    public void Tree_WithoutMissingBranch_TreatsNaNAsZero()
    {
        var tree = new CarRankingModel.Tree(
        [
            new(false, 0, -1.0, false, "none", 1, 2, 0),
            new(true, 0, 0, false, null, 0, 0, 10.0),
            new(true, 0, 0, false, null, 0, 0, 20.0)
        ]);

        tree.Predict([double.NaN]).Should().Be(20.0);   // NaN → 0 > −1 → right
    }

    // ── Specs and features ──────────────────────────────────────────────────

    [Fact]
    public void EveryDemoFleetCar_HasSpecs()
    {
        var specs = CarSpecCatalog.Load(SpecsPath);
        var seeded = new[] { ("Toyota", "Camry"), ("Honda", "CR-V"), ("Volkswagen", "Golf"), ("Toyota", "Sienna"), ("BMW", "4 Series"), ("Nissan", "Leaf") }
            .Select(m => new Car { Brand = m.Item1, Model = m.Item2 });

        var missing = DemoFleet.Create().Concat(seeded).Where(c => specs.Find(c) is null).Select(c => $"{c.Brand} {c.Model}").ToList();

        missing.Should().BeEmpty();
    }

    [Fact]
    public void CarVectorAndText_UseSpecs()
    {
        var car = new Car
        {
            Brand = "Skoda", Model = "Octavia", Year = 2023, Seats = 5, PricePerDay = 1100, FuelType = FuelType.Diesel,
            Transmission = Transmission.Automatic, Category = new Category { Name = "Седан" }, Description = "Практичний седан."
        };
        var spec = CarSpecCatalog.Load(SpecsPath).Find(car);

        var x = CarRankingFeatures.CarVector(car, spec);
        var named = CarRankingFeatures.CarFeatureNames.Zip(x).ToDictionary(p => p.First, p => p.Second);

        named["trunk_l"].Should().Be(600);
        named["is_diesel"].Should().Be(1);
        named["cat_sedan"].Should().Be(1);
        named["ev_range_km"].Should().Be(double.NaN);
        named["log_price"].Should().BeApproximately(Math.Log(1100), 1e-12);
        CarRankingFeatures.CarText(car, spec).Should().Contain("Багажник 600 л").And.Contain("Витрата 4.6 л/100 км").And.EndWith("Ціна 1100 грн за добу.");
    }

    // ── Search tool ─────────────────────────────────────────────────────────

    private static float[] PseudoEmbedding(string text)
    {
        var rng = new Random(text.GetHashCode(StringComparison.Ordinal));
        return Enumerable.Range(0, 3072).Select(_ => (float)(rng.NextDouble() - 0.5)).ToArray();
    }

    private static CarRanker CreateRanker(IEmbeddingGeneratorFactory embeddings, AiOptions? options = null)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(WebRoot);
        return new CarRanker(Options.Create(options ?? new AiOptions()), embeddings, new QueryEmbeddingCache(), NullLogger<CarRanker>.Instance, env.Object);
    }

    private BookingTools CreateTools(CarRanker? ranker) =>
        new(CreateUnitOfWork(_db), CreateCarService(_db), Mock.Of<IKnowledgeSearch>(), CreateClock(), new AiOptions(),
            new AgentTurnContext(Guid.NewGuid(), null, Today), ranker);

    [Fact]
    public async Task Search_WithWishes_RanksAllAvailableCarsByTheModelAndAddsSpecs()
    {
        var ranker = CreateRanker(new FakeEmbeddingFactory(new FakeEmbeddingGenerator(PseudoEmbedding), "gemini:gemini-embedding-001"));
        var byPrice = (CarSearchResult)await CreateTools(null).SearchAvailableCarsAsync(Pickup, Return);

        var result = (CarSearchResult)await CreateTools(ranker).SearchAvailableCarsAsync(Pickup, Return, wishes: "нас семеро і багато валіз");

        ranker.IsAvailable.Should().BeTrue();
        result.TotalFound.Should().Be(byPrice.TotalFound);
        result.Cars.Should().OnlyContain(c => c.Specs != null && c.Specs.Contains("trunk"));
        result.Note.Should().Contain("fit the wishes");

        var cars = (await CreateUnitOfWork(_db).Cars.GetAllWithCategoryAsync())
            .Where(c => result.Cars.Select(o => o.Id).Contains(c.Id) || byPrice.Cars.Select(o => o.Id).Contains(c.Id)).ToList();
        var expected = await ranker.RankAsync("нас семеро і багато валіз", cars);
        result.Cars[0].Id.Should().Be(expected![0].Car.Id);
    }

    [Fact]
    public async Task Search_WithWishes_WhenEmbeddingsFail_KeepsPriceOrderAndSaysSo()
    {
        var ranker = CreateRanker(new FakeEmbeddingFactory(null, "gemini:gemini-embedding-001"));
        var byPrice = (CarSearchResult)await CreateTools(null).SearchAvailableCarsAsync(Pickup, Return);

        var result = (CarSearchResult)await CreateTools(ranker).SearchAvailableCarsAsync(Pickup, Return, wishes: "економне для міста");

        result.Cars.Select(c => c.Id).Should().Equal(byPrice.Cars.Select(c => c.Id));
        result.Cars.Should().OnlyContain(c => c.Specs == null);
        result.Note.Should().Contain("ordered by price");
    }

    [Fact]
    public async Task Ranker_WithAnotherEmbeddingModel_DoesNotRank()
    {
        var ranker = CreateRanker(new FakeEmbeddingFactory(new FakeEmbeddingGenerator(PseudoEmbedding), "test:other-embedding"));
        var cars = (await CreateUnitOfWork(_db).Cars.GetAllWithCategoryAsync()).ToList();

        (await ranker.RankAsync("для міста", cars)).Should().BeNull();
    }

    [Fact]
    public async Task Ranker_CanBeSwitchedOff()
    {
        var options = new AiOptions();
        options.CarRanking.Enabled = false;
        var ranker = CreateRanker(new FakeEmbeddingFactory(new FakeEmbeddingGenerator(PseudoEmbedding), "gemini:gemini-embedding-001"), options);
        var cars = (await CreateUnitOfWork(_db).Cars.GetAllWithCategoryAsync()).ToList();

        (await ranker.RankAsync("для міста", cars)).Should().BeNull();
    }
}
