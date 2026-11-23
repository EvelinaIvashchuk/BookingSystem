using System.Globalization;
using System.Text;
using System.Text.Json;
using BookingSystem.Enums;
using BookingSystem.Models;

namespace BookingSystem.Ai.Ranking;

/// <summary>Model-level specifications of a car (trunk, consumption…), from <c>CarRanking/car_specs.json</c>.</summary>
public sealed record CarSpec(
    string Brand,
    string Model,
    int? TrunkL,
    double? Consumption,
    int? EvRangeKm,
    bool Awd,
    int? LengthMm,
    int? PowerHp,
    bool Premium);

/// <summary>Specifications by make and model (the same for every car of that model).</summary>
public sealed class CarSpecCatalog
{
    private readonly Dictionary<string, CarSpec> _byModel;

    public CarSpecCatalog(IEnumerable<CarSpec> specs) =>
        _byModel = specs.ToDictionary(s => Key(s.Brand, s.Model), StringComparer.OrdinalIgnoreCase);

    public static CarSpecCatalog Empty { get; } = new([]);

    public int Count => _byModel.Count;

    public CarSpec? Find(Car car) => _byModel.GetValueOrDefault(Key(car.Brand, car.Model));

    public static CarSpecCatalog Load(string path)
    {
        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<SpecFile>(stream, JsonOptions)
                   ?? throw new InvalidDataException($"Empty car specs file: {path}");
        return new CarSpecCatalog(file.Models);
    }

    private static string Key(string brand, string model) => $"{brand.Trim()}|{model.Trim()}";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record SpecFile(List<CarSpec> Models);
}

/// <summary>
/// Car features for the wish-based ranking model. The same code produces the training data
/// (BookingSystem.Evaluation <c>ranking-data</c>) and the features at run time, so both always agree.
/// </summary>
public static class CarRankingFeatures
{
    /// <summary>Car-side feature names, in the order of <see cref="CarVector"/>. Unknown values are NaN.</summary>
    public static readonly IReadOnlyList<string> CarFeatureNames =
    [
        "log_price", "seats", "trunk_l", "consumption", "ev_range_km", "is_electric", "is_hybrid", "is_diesel",
        "awd", "length_mm", "power_hp", "automatic", "cat_sedan", "cat_suv", "cat_hatchback", "cat_minivan", "cat_coupe",
        "premium", "year"
    ];

    public static double[] CarVector(Car car, CarSpec? spec)
    {
        var category = CategoryKey(car.Category?.Name);
        return
        [
            Math.Log((double)car.PricePerDay),
            car.Seats,
            spec?.TrunkL ?? double.NaN,
            spec?.Consumption ?? double.NaN,
            spec?.EvRangeKm ?? double.NaN,
            car.FuelType == FuelType.Electric ? 1 : 0,
            car.FuelType == FuelType.Hybrid ? 1 : 0,
            car.FuelType == FuelType.Diesel ? 1 : 0,
            spec is null ? double.NaN : spec.Awd ? 1 : 0,
            spec?.LengthMm ?? double.NaN,
            spec?.PowerHp ?? double.NaN,
            car.Transmission == Transmission.Automatic ? 1 : 0,
            category == "sedan" ? 1 : 0,
            category == "suv" ? 1 : 0,
            category == "hatchback" ? 1 : 0,
            category == "minivan" ? 1 : 0,
            category == "coupe" ? 1 : 0,
            spec is null ? double.NaN : spec.Premium ? 1 : 0,
            car.Year
        ];
    }

    /// <summary>Text of the car that is embedded and compared with the user's wish.</summary>
    public static string CarText(Car car, CarSpec? spec)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append($"{car.Brand} {car.Model} ({car.Year}), {car.Category?.Name ?? "авто"}. ");
        sb.Append($"{car.Seats} місць, {(car.Transmission == Transmission.Automatic ? "автоматична коробка" : "механічна коробка")}, {FuelUk(car.FuelType)}. ");
        if (spec is not null)
        {
            if (spec.TrunkL is { } trunk) sb.Append($"Багажник {trunk} л. ");
            if (spec.Consumption is { } cons) sb.Append(ci, $"Витрата {cons:0.0} л/100 км. ");
            if (spec.EvRangeKm is { } range) sb.Append($"Запас ходу {range} км. ");
            sb.Append(spec.Awd ? "Повний привід. " : "Привід на одну вісь. ");
            if (spec.LengthMm is { } len) sb.Append(ci, $"Довжина {len / 1000.0:0.00} м. ");
            if (spec.PowerHp is { } hp) sb.Append($"Потужність {hp} к.с. ");
            if (spec.Premium) sb.Append("Преміум-клас. ");
        }
        if (!string.IsNullOrWhiteSpace(car.Description)) sb.Append(car.Description.Trim()).Append(' ');
        sb.Append(ci, $"Ціна {car.PricePerDay:0} грн за добу.");
        return sb.ToString();
    }

    public static string CategoryKey(string? name) => (name ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "седан" or "sedan" => "sedan",
        "suv" or "позашляховик" or "кросовер" => "suv",
        "хетчбек" or "hatchback" => "hatchback",
        "мінівен" or "minivan" => "minivan",
        "купе" or "coupe" => "coupe",
        _ => "other"
    };

    private static string FuelUk(FuelType fuel) => fuel switch
    {
        FuelType.Petrol => "бензин",
        FuelType.Diesel => "дизель",
        FuelType.Electric => "електро",
        FuelType.Hybrid => "гібрид",
        _ => fuel.ToString()
    };

    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }
}
