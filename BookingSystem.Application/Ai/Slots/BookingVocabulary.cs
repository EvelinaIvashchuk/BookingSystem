using System.Globalization;
using BookingSystem.Enums;

namespace BookingSystem.Ai.Slots;

/// <summary>
/// Maps free-form values produced by the model or the user (Ukrainian / English, any case,
/// common synonyms) to canonical domain values. Used by the agent tools and the slot validator.
/// </summary>
public static class BookingVocabulary
{
    private static readonly Dictionary<string, Transmission> TransmissionSynonyms = Build(new Dictionary<Transmission, string[]>
    {
        [Transmission.Automatic] = ["automatic", "auto", "at", "автомат", "автоматична", "автоматична кпп", "автоматична коробка", "акпп", "автомат кпп", "робот", "варіатор", "cvt"],
        [Transmission.Manual] = ["manual", "mt", "stick", "механіка", "механічна", "механічна кпп", "механічна коробка", "мкпп", "ручна"]
    });

    private static readonly Dictionary<string, FuelType> FuelSynonyms = Build(new Dictionary<FuelType, string[]>
    {
        [FuelType.Petrol] = ["petrol", "gasoline", "gas", "бензин", "бензиновий", "бензинова"],
        [FuelType.Diesel] = ["diesel", "дизель", "дизельний", "дизельна", "дп"],
        [FuelType.Electric] = ["electric", "ev", "електро", "електричний", "електромобіль", "електрокар"],
        [FuelType.Hybrid] = ["hybrid", "гібрид", "гібридний", "гібридна"]
    });

    /// <summary>Canonical category names as stored in the database (Category.Name).</summary>
    private static readonly Dictionary<string, string> CategorySynonyms = Build(new Dictionary<string, string[]>
    {
        ["Седан"] = ["седан", "sedan", "saloon"],
        ["SUV"] = ["suv", "кросовер", "crossover", "позашляховик", "джип", "jeep", "off-road"],
        ["Хетчбек"] = ["хетчбек", "хечбек", "hatchback", "hatch", "компакт", "compact"],
        ["Мінівен"] = ["мінівен", "мiнiвен", "minivan", "van", "мінівен на 7 місць", "сімейний"],
        ["Купе"] = ["купе", "coupe", "coupé", "спорткар", "sports car", "sport"]
    });

    /// <summary>Canonical (Ukrainian, nominative) city names as used in Car.Location.</summary>
    private static readonly Dictionary<string, string> CitySynonyms = Build(new Dictionary<string, string[]>
    {
        ["Київ"] = ["київ", "києві", "києва", "kyiv", "kiev", "киев"],
        ["Львів"] = ["львів", "львові", "львова", "lviv", "lvov", "львов"],
        ["Одеса"] = ["одеса", "одесі", "одеси", "odesa", "odessa", "одесса"],
        ["Харків"] = ["харків", "харкові", "харкова", "kharkiv", "kharkov", "харьков"],
        ["Дніпро"] = ["дніпро", "дніпрі", "dnipro", "днепр"],
        ["Чернівці"] = ["чернівці", "чернівцях", "chernivtsi", "черновцы"],
        ["Івано-Франківськ"] = ["івано-франківськ", "івано-франківську", "ivano-frankivsk", "франківськ"],
        ["Ужгород"] = ["ужгород", "ужгороді", "uzhhorod"]
    });

    public static bool TryParseTransmission(string? value, out Transmission transmission) =>
        TryMap(TransmissionSynonyms, value, out transmission) || Enum.TryParse(value?.Trim(), true, out transmission);

    public static bool TryParseFuelType(string? value, out FuelType fuelType) =>
        TryMap(FuelSynonyms, value, out fuelType) || Enum.TryParse(value?.Trim(), true, out fuelType);

    /// <summary>Returns the canonical category name if the value names a known category.</summary>
    public static bool TryNormalizeCategory(string? value, out string category) =>
        TryMap(CategorySynonyms, value, out category!);

    /// <summary>
    /// Returns the canonical city name for known cities; unknown values are returned trimmed
    /// so that searching by other cities still works.
    /// </summary>
    public static string NormalizeCity(string value) =>
        TryMap(CitySynonyms, value, out var city) ? city : value.Trim();

    public static IReadOnlyList<string> TransmissionValues => Enum.GetNames<Transmission>();
    public static IReadOnlyList<string> FuelTypeValues => Enum.GetNames<FuelType>();
    public static IReadOnlyList<string> CategoryValues => [.. CategorySynonyms.Values.Distinct()];

    private static string Key(string value) =>
        value.Trim().ToLower(CultureInfo.InvariantCulture).Replace('’', '\'').Replace('ʼ', '\'');

    private static bool TryMap<T>(Dictionary<string, T> map, string? value, out T result)
    {
        result = default!;
        return !string.IsNullOrWhiteSpace(value) && map.TryGetValue(Key(value), out result!);
    }

    private static Dictionary<string, T> Build<T>(Dictionary<T, string[]> groups) where T : notnull
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (canonical, synonyms) in groups)
        {
            map[Key(canonical.ToString()!)] = canonical;
            foreach (var synonym in synonyms)
                map[Key(synonym)] = canonical;
        }
        return map;
    }
}
