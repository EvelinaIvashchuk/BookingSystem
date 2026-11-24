using System.Text.Json;
using System.Text.Json.Serialization;
using BookingSystem.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BookingSystem.Ai.Ranking;

/// <summary>
/// Ranking model trained by <c>tools/ranking/train.py</c> (LightGBM, LambdaRank) and exported to JSON:
/// PCA of the wish embedding and gradient-boosted trees over [cosine(wish, car), wish PCA components, car features].
/// Evaluated here without any ML runtime: the trees are a few hundred comparisons.
/// </summary>
public sealed class CarRankingModel
{
    public const string FormatName = "car-ranker/1";

    public required string EmbeddingModel { get; init; }
    public required IReadOnlyList<string> FeatureNames { get; init; }
    public required float[] PcaMean { get; init; }
    public required float[][] PcaComponents { get; init; }
    public required IReadOnlyList<Tree> Trees { get; init; }

    public int PcaDimensions => PcaComponents.Length;

    public double Score(ReadOnlySpan<double> features)
    {
        double sum = 0;
        foreach (var tree in Trees) sum += tree.Predict(features);
        return sum;
    }

    /// <summary>Projects a wish embedding onto the principal components fitted on the training wishes.</summary>
    public double[] Project(ReadOnlySpan<float> embedding)
    {
        var result = new double[PcaComponents.Length];
        if (embedding.Length != PcaMean.Length) return result;
        for (var k = 0; k < PcaComponents.Length; k++)
        {
            var component = PcaComponents[k];
            double s = 0;
            for (var i = 0; i < embedding.Length; i++) s += (embedding[i] - PcaMean[i]) * (double)component[i];
            result[k] = s;
        }
        return result;
    }

    public static CarRankingModel Load(string path)
    {
        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<ModelFile>(stream, JsonOptions)
                   ?? throw new InvalidDataException($"Empty ranking model: {path}");
        if (file.Format != FormatName)
            throw new InvalidDataException($"Unsupported ranking model format '{file.Format}' in {path}");

        return new CarRankingModel
        {
            EmbeddingModel = file.EmbeddingModel,
            FeatureNames = file.FeatureNames,
            PcaMean = file.Pca.Mean,
            PcaComponents = file.Pca.Components,
            Trees = file.Trees.Select(t => new Tree(t.Nodes)).ToList()
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public sealed class Tree(IReadOnlyList<Node> nodes)
    {
        public double Predict(ReadOnlySpan<double> x)
        {
            var node = nodes[0];
            while (!node.Leaf)
            {
                var value = x[node.Feature];
                bool left;
                switch (node.Missing)
                {
                    case "nan" when double.IsNaN(value):
                        left = node.DefaultLeft;
                        break;
                    case "zero" when double.IsNaN(value) || Math.Abs(value) <= 1e-35:
                        left = node.DefaultLeft;
                        break;
                    default:
                        if (double.IsNaN(value)) value = 0;   // LightGBM: NaN is treated as 0 when the split has no missing branch
                        left = value <= node.Threshold;
                        break;
                }
                node = nodes[left ? node.Left : node.Right];
            }
            return node.Value;
        }
    }

    /// <summary>Tree node: a split (<see cref="Feature"/> &lt;= <see cref="Threshold"/> goes left) or a leaf.</summary>
    public sealed record Node(
        bool Leaf, int Feature, double Threshold, bool DefaultLeft, string? Missing, int Left, int Right, double Value);

    private sealed record ModelFile(
        string Format, string EmbeddingModel, List<string> FeatureNames, PcaFile Pca, List<TreeFile> Trees);

    private sealed record PcaFile(float[] Mean, float[][] Components);

    private sealed record TreeFile(List<Node> Nodes);
}

public sealed record RankedCar(Car Car, double Score, CarSpec? Spec);

/// <summary>
/// Ranks available cars by how well they fit the user's free-text wishes ("дві великі валізи, економне").
/// Returns <c>null</c> when ranking is switched off or impossible (no model, embeddings unavailable): the caller then
/// keeps the usual order by price.
/// </summary>
public sealed class CarRanker(
    IOptions<AiOptions> options,
    IEmbeddingGeneratorFactory embeddings,
    Knowledge.QueryEmbeddingCache cache,
    ILogger<CarRanker> logger,
    IHostEnvironment? environment = null)
{
    private readonly CarRankingOptions _options = options.Value.CarRanking;
    private readonly Lazy<(CarRankingModel? Model, CarSpecCatalog Specs)> _loaded = new(() => LoadFiles(options.Value.CarRanking, environment, logger));
    private ICarScorer? _lightGbm;
    private readonly Lazy<ICarScorer?> _mlp = new(() => LoadNeural(options.Value.CarRanking.MlpModelPath, environment, logger, MlpScorer.Load));
    private readonly Lazy<ICarScorer?> _twoTower = new(() => LoadNeural(options.Value.CarRanking.TwoTowerModelPath, environment, logger, TwoTowerScorer.Load));

    // LightGBM is read together with the specs (LoadFiles); expose it as a scorer.
    private ICarScorer? LightGbm => _lightGbm ??= _loaded.Value.Model is { } model ? new LightGbmScorer(model) : null;

    public CarSpecCatalog Specs => _loaded.Value.Specs;

    public bool IsAvailable => _options.Enabled && _loaded.Value.Model is not null;

    /// <summary>Ranking models whose files exist (the weights are not loaded), with their quality on the test wishes.</summary>
    public IReadOnlyList<RankingModelInfo> AvailableModels()
    {
        if (!_options.Enabled) return [];
        var result = new List<RankingModelInfo>();
        foreach (var id in RankingModelIds.All)
        {
            if (Resolve(PathOf(id), environment) is { } path)
                result.Add(new RankingModelInfo(id, RankingModelFiles.ReadTestNdcg5(path)));
        }
        return result;
    }

    private string PathOf(string id) => id switch
    {
        RankingModelIds.Mlp => _options.MlpModelPath,
        RankingModelIds.TwoTower => _options.TwoTowerModelPath,
        _ => _options.ModelPath
    };

    private ICarScorer? Scorer(string? id) => id switch
    {
        RankingModelIds.Mlp => _mlp.Value,
        RankingModelIds.TwoTower => _twoTower.Value,
        _ => LightGbm
    };

    /// <param name="modelId">Ranking model (<see cref="RankingModelIds"/>); <c>null</c> means <see cref="CarRankingOptions.DefaultModel"/>.
    /// A model whose file cannot be loaded falls back to LightGBM.</param>
    public async Task<IReadOnlyList<RankedCar>?> RankAsync(string wishes, IReadOnlyList<Car> cars, CancellationToken ct = default, string? modelId = null)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(wishes) || cars.Count == 0) return null;
        var specs = _loaded.Value.Specs;
        var requested = string.IsNullOrWhiteSpace(modelId) ? _options.DefaultModel : modelId;
        var scorer = Scorer(requested);
        if (scorer is null && requested != RankingModelIds.LightGbm)
        {
            logger.LogWarning("Ranking model {Model} is unavailable; using {Fallback}", requested, RankingModelIds.LightGbm);
            scorer = LightGbm;
        }
        if (scorer is null) return null;

        if (!string.Equals(scorer.EmbeddingModel, embeddings.ModelId, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Car ranking model {Model} was trained with {Trained}, but the embedding model is {Current}; ranking skipped",
                scorer.Id, scorer.EmbeddingModel, embeddings.ModelId);
            return null;
        }

        try
        {
            var generator = embeddings.Create();
            var wish = wishes.Trim();
            var wishVector = await cache.GetOrAddAsync(embeddings.ModelId, wish, async () =>
                (await generator.GenerateAsync([wish], cancellationToken: ct))[0].Vector.ToArray());

            var texts = cars.Select(c => CarRankingFeatures.CarText(c, specs.Find(c))).ToList();
            var carVectors = await EmbedAllAsync(generator, texts, ct);

            var carSpecs = cars.Select(specs.Find).ToList();
            var cosines = carVectors.Select(v => CarRankingFeatures.Cosine(wishVector, v)).ToList();
            var features = cars.Select((c, i) => CarRankingFeatures.CarVector(c, carSpecs[i])).ToList();
            var scores = scorer.Score(wishVector, cosines, features);
            var ranked = cars.Select((c, i) => new RankedCar(c, scores[i], carSpecs[i])).ToList();

            return [.. ranked.OrderByDescending(r => r.Score).ThenBy(r => r.Car.PricePerDay).ThenBy(r => r.Car.Id)];
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Car ranking by wishes failed; falling back to the order by price");
            return null;
        }
    }

    /// <summary>Feature vector in the order the model was trained with.</summary>
    public static double[] Features(CarRankingModel model, double cosine, IReadOnlyList<double> wishComponents, IReadOnlyList<double> carVector)
    {
        var byName = new Dictionary<string, double>(StringComparer.Ordinal) { ["cos_wish_car"] = cosine };
        for (var k = 0; k < wishComponents.Count; k++) byName[$"wish_pc{k}"] = wishComponents[k];
        for (var j = 0; j < CarRankingFeatures.CarFeatureNames.Count; j++) byName[CarRankingFeatures.CarFeatureNames[j]] = carVector[j];
        return [.. model.FeatureNames.Select(n => byName.TryGetValue(n, out var v) ? v : double.NaN)];
    }

    private async Task<List<float[]>> EmbedAllAsync(
        Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>> generator,
        List<string> texts, CancellationToken ct)
    {
        var result = new float[texts.Count][];
        var missing = new List<int>();
        for (var i = 0; i < texts.Count; i++)
        {
            if (cache.TryGet(embeddings.ModelId, texts[i], out var cached)) result[i] = cached;
            else missing.Add(i);
        }

        foreach (var batch in missing.Chunk(50))
        {
            var vectors = await generator.GenerateAsync(batch.Select(i => texts[i]), cancellationToken: ct);
            for (var j = 0; j < batch.Length; j++)
            {
                result[batch[j]] = vectors[j].Vector.ToArray();
                cache.Set(embeddings.ModelId, texts[batch[j]], result[batch[j]]);
            }
        }
        return [.. result];
    }

    private static ICarScorer? LoadNeural(string path, IHostEnvironment? environment, ILogger logger, Func<string, ICarScorer> load)
    {
        try
        {
            if (Resolve(path, environment) is { } resolved) return load(resolved);
            logger.LogWarning("Neural ranking model {Path} not found", path);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or FormatException or KeyNotFoundException)
        {
            logger.LogError(ex, "Neural ranking model {Path} could not be loaded", path);
        }
        return null;
    }

    private static (CarRankingModel?, CarSpecCatalog) LoadFiles(CarRankingOptions options, IHostEnvironment? environment, ILogger logger)
    {
        var specs = CarSpecCatalog.Empty;
        CarRankingModel? model = null;
        try
        {
            if (Resolve(options.SpecsPath, environment) is { } specsPath) specs = CarSpecCatalog.Load(specsPath);
            else logger.LogWarning("Car specs file {Path} not found", options.SpecsPath);

            if (Resolve(options.ModelPath, environment) is { } modelPath) model = CarRankingModel.Load(modelPath);
            else logger.LogWarning("Car ranking model {Path} not found; wishes are not used for ranking", options.ModelPath);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            logger.LogError(ex, "Car ranking files could not be loaded");
        }
        return (model, specs);
    }

    private static string? Resolve(string path, IHostEnvironment? environment)
    {
        if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;
        foreach (var root in new[] { environment?.ContentRootPath ?? Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var candidate = Path.Combine(root, path);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
