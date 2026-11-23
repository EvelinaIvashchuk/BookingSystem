using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookingSystem.Ai;
using BookingSystem.Ai.Ranking;
using BookingSystem.Data;
using BookingSystem.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookingSystem.Evaluation;

/// <summary>
/// Data for the wish-based car ranking model (E5), written for <c>tools/ranking/*.py</c>:
/// <list type="bullet">
/// <item><c>results/ranking/cars.csv</c> — rentable cars of the evaluation fleet with their specifications, the text that
///       is embedded and the car features exactly as <see cref="CarRankingFeatures"/> computes them at run time;</item>
/// <item><c>results/ranking/cache/embeddings.jsonl</c> — embeddings of the car texts and of every wish in
///       <c>datasets/car_wishes.json</c> (computed once, reused on later runs).</item>
/// </list>
/// </summary>
public sealed class RankingData(IServiceScopeFactory scopes, IEmbeddingGeneratorFactory embeddings, CarRanker ranker, ILogger<RankingData> logger)
{
    public async Task RunAsync(EvalPaths paths, CancellationToken ct)
    {
        var dir = Path.Combine(paths.Results, "ranking");
        Directory.CreateDirectory(Path.Combine(dir, "cache"));

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cars = await db.Cars.Include(c => c.Category).AsNoTracking()
            .Where(c => c.Status == CarStatus.Available).OrderBy(c => c.Id).ToListAsync(ct);
        if (ranker.Specs.Count == 0) throw new InvalidOperationException("Car specs were not loaded (Ai:CarRanking:SpecsPath).");

        var header = new List<string>
        {
            "car_id", "name", "brand", "model", "year", "category", "seats", "transmission", "fuel", "price", "location",
            "trunk_l", "consumption", "ev_range_km", "awd", "length_mm", "power_hp", "premium", "text"
        };
        header.AddRange(CarRankingFeatures.CarFeatureNames.Select(n => "f_" + n));

        var rows = new List<object?[]>();
        var texts = new List<string>();
        foreach (var car in cars)
        {
            var spec = ranker.Specs.Find(car);
            if (spec is null) logger.LogWarning("No specs for {Car}", car.FullName);
            var text = CarRankingFeatures.CarText(car, spec);
            texts.Add(text);
            var row = new List<object?>
            {
                car.Id, car.FullName, car.Brand, car.Model, car.Year, CarRankingFeatures.CategoryKey(car.Category?.Name), car.Seats,
                car.Transmission, car.FuelType, car.PricePerDay.ToString(CultureInfo.InvariantCulture), car.Location.Split(',')[0],
                spec?.TrunkL, spec?.Consumption?.ToString(CultureInfo.InvariantCulture), spec?.EvRangeKm, spec?.Awd == true ? 1 : 0,
                spec?.LengthMm, spec?.PowerHp, spec?.Premium == true ? 1 : 0, text
            };
            row.AddRange(CarRankingFeatures.CarVector(car, spec).Select(v => (object?)(double.IsNaN(v) ? "" : v.ToString("R", CultureInfo.InvariantCulture))));
            rows.Add([.. row]);
        }
        Csv.Write(Path.Combine(dir, "cars.csv"), header, rows);
        logger.LogInformation("Ranking data: {Cars} rentable cars written ({WithSpecs} with specs)", cars.Count, cars.Count(c => ranker.Specs.Find(c) is not null));

        var wishesPath = Path.Combine(paths.Datasets, "car_wishes.json");
        if (File.Exists(wishesPath))
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(wishesPath, ct));
            foreach (var wish in doc.RootElement.GetProperty("wishes").EnumerateArray())
                texts.Add(wish.GetProperty("text").GetString()!.Trim());
        }

        var added = await EmbedAsync(Path.Combine(dir, "cache", "embeddings.jsonl"), texts.Distinct().ToList(), ct);
        logger.LogInformation("Ranking data: {Texts} texts, {Added} new embeddings ({Model})", texts.Count, added, embeddings.ModelId);
    }

    /// <summary>Appends embeddings of texts that are not in the cache yet; one JSON line per text (vector as base64 float32).</summary>
    private async Task<int> EmbedAsync(string cachePath, List<string> texts, CancellationToken ct)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(cachePath))
            foreach (var line in await File.ReadAllLinesAsync(cachePath, ct))
                if (line.Length > 0)
                    known.Add(JsonDocument.Parse(line).RootElement.GetProperty("key").GetString()!);

        var missing = texts.Where(t => !known.Contains(Key(t))).ToList();
        if (missing.Count == 0) return 0;

        var generator = embeddings.Create();
        await using var writer = new StreamWriter(cachePath, append: true, new UTF8Encoding(false));
        foreach (var batch in missing.Chunk(50))
        {
            var vectors = await generator.GenerateAsync(batch, cancellationToken: ct);
            for (var i = 0; i < batch.Length; i++)
            {
                var floats = vectors[i].Vector.ToArray();
                var bytes = new byte[floats.Length * 4];
                Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    key = Key(batch[i]), model = embeddings.ModelId, text = batch[i], dim = floats.Length, vector = Convert.ToBase64String(bytes)
                }));
            }
            await writer.FlushAsync(ct);
        }
        return missing.Count;
    }

    private string Key(string text) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(embeddings.ModelId + "\u001f" + text.Trim())));
}
