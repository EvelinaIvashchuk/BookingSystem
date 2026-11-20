using System.Text.RegularExpressions;
using BookingSystem.Models;

namespace BookingSystem.Ai.Tools;

/// <summary>
/// Matches a car name written by the user or the model ("Toyota Camry", "camry 2024", "Тойота Камрі", "BMW 4")
/// against a car of the fleet. Every meaningful word of the query must match a word of "brand model":
/// numbers exactly, words as a prefix of at least two letters. Common Ukrainian spellings of the fleet's brands and
/// models are mapped to Latin ones.
/// <para>
/// Used by <c>search_available_cars</c> (filter by name, E1 failure case 2: a weak model searched without a filter,
/// did not see the Camry among the 5 cheapest cars and said it was booked) and by <c>calculate_price</c> /
/// <c>create_rental_draft</c> (consistency of <c>carId</c> with the name the model shows the user, failure case 3).
/// </para>
/// </summary>
public static partial class CarNameMatcher
{
    private static readonly HashSet<string> StopWords =
    [
        "авто", "автомобіль", "автомобіля", "машина", "машину", "машини", "car", "the", "a", "an", "рік", "року", "year", "р"
    ];

    private static readonly Dictionary<string, string> Ukrainian = new()
    {
        ["тойота"] = "toyota", ["хонда"] = "honda", ["фольксваген"] = "volkswagen", ["фольцваген"] = "volkswagen",
        ["бмв"] = "bmw", ["ніссан"] = "nissan", ["нісан"] = "nissan", ["шкода"] = "skoda", ["хюндай"] = "hyundai",
        ["хендай"] = "hyundai", ["хьюндай"] = "hyundai", ["кіа"] = "kia", ["рено"] = "renault", ["мітсубісі"] = "mitsubishi",
        ["міцубісі"] = "mitsubishi", ["вольво"] = "volvo", ["пежо"] = "peugeot", ["форд"] = "ford", ["мерседес"] = "mercedes",
        ["мерс"] = "mercedes", ["сітроен"] = "citroen", ["ауді"] = "audi", ["тесла"] = "tesla", ["мазда"] = "mazda",
        ["бід"] = "byd", ["сузукі"] = "suzuki", ["опель"] = "opel", ["міні"] = "mini", ["шевроле"] = "chevrolet",
        ["порше"] = "porsche",
        ["камрі"] = "camry", ["королла"] = "corolla", ["корола"] = "corolla", ["октавія"] = "octavia",
        ["пассат"] = "passat", ["пасат"] = "passat", ["дастер"] = "duster", ["спортейдж"] = "sportage", ["аутлендер"] = "outlander",
        ["сід"] = "ceed", ["кліо"] = "clio", ["фабія"] = "fabia", ["туран"] = "touran", ["галаксі"] = "galaxy",
        ["берлінго"] = "berlingo", ["мустанг"] = "mustang", ["кашкай"] = "qashqai", ["туксон"] = "tucson", ["таксон"] = "tucson",
        ["суперб"] = "superb", ["сівік"] = "civic", ["сіл"] = "seal", ["прадо"] = "prado", ["крузер"] = "cruiser",
        ["кона"] = "kona", ["вітара"] = "vitara", ["куга"] = "kuga", ["ярис"] = "yaris", ["яріс"] = "yaris", ["корса"] = "corsa",
        ["поло"] = "polo", ["зое"] = "zoe", ["купер"] = "cooper", ["карнівал"] = "carnival", ["сценік"] = "scenic",
        ["камаро"] = "camaro", ["кайман"] = "cayman", ["елантра"] = "elantra", ["сієнна"] = "sienna", ["сієна"] = "sienna",
        ["ліф"] = "leaf", ["гольф"] = "golf", ["спортидж"] = "sportage", ["модель"] = "model", ["серія"] = "series",
        ["серії"] = "series", ["купе"] = "coupe"
    };

    /// <summary>
    /// True when every meaningful word of <paramref name="query"/> matches the car. A year ("(2024)") is ignored:
    /// models often repeat it wrongly, and the fleet has no two cars that differ only by year.
    /// </summary>
    public static bool Matches(Car car, string? query)
    {
        var wanted = Words(query).Where(w => !IsYear(w)).ToList();
        if (wanted.Count == 0) return false;

        var words = Words($"{car.Brand} {car.Model}");
        return wanted.All(w => words.Any(c => WordMatches(w, c)));
    }

    private static bool IsYear(string word) => word.Length == 4 && int.TryParse(word, out var y) && y is >= 1950 and <= 2100;

    private static bool WordMatches(string wanted, string word) =>
        char.IsDigit(wanted[0])
            ? wanted == word
            : word == wanted || (wanted.Length >= 2 && word.StartsWith(wanted, StringComparison.Ordinal));

    private static List<string> Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        return WordRegex().Matches(text.ToLowerInvariant().Replace('’', '\'').Replace('ʼ', '\''))
            .Select(m => m.Value)
            .Where(w => !StopWords.Contains(w))
            .Select(w => Ukrainian.GetValueOrDefault(w, w))
            // "RAV4", "A5", "ID.3": letters and digits glued together are split ("rav", "4").
            .SelectMany(w => LetterDigitRegex().Matches(w).Select(m => m.Value))
            .ToList();
    }

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\p{L}[\p{L}']*|\p{N}+")]
    private static partial Regex LetterDigitRegex();
}
