using System.Text.RegularExpressions;

namespace BookingSystem.Ai.Knowledge;

/// <summary>
/// Features of a query that drive the adaptive fusion weights:
/// <list type="bullet">
/// <item><c>n</c> — number of content words (after stop-word removal);</item>
/// <item><c>d</c> — share of those words that are domain terms (депозит, франшиза, КАСКО, стаж...);</item>
/// <item><c>num</c> — the query contains a number or an amount;</item>
/// <item>predicted category — by keyword map (no prediction on ties or no matches).</item>
/// </list>
/// </summary>
public sealed partial record QueryFeatures(
    int WordCount,
    double DomainTermShare,
    bool HasNumber,
    string? PredictedCategory,
    IReadOnlyList<string> Stems)
{
    public static QueryFeatures Extract(string query, DomainVocabulary vocabulary)
    {
        var stems = TextNormalizer.ContentStems(query);
        var n = stems.Count;
        var domain = n == 0 ? 0 : stems.Count(vocabulary.IsDomainTerm) / (double)n;

        return new QueryFeatures(n, domain, NumberRegex().IsMatch(query), vocabulary.PredictCategory(stems), stems);
    }

    /// <summary>
    /// w_ft = clamp(a + b·d − c·max(0, n − n0) + e·num, min, max); w_vec = 1 − w_ft.
    /// Short term-heavy queries lean on full-text search, long descriptive ones on vectors.
    /// </summary>
    public (double FullText, double Vector) Weights(AdaptiveFusionOptions o)
    {
        var raw = o.A
                  + o.B * DomainTermShare
                  - o.C * Math.Max(0, WordCount - o.N0)
                  + o.E * (HasNumber ? 1 : 0);
        var ft = Math.Clamp(raw, o.MinWeight, o.MaxWeight);
        return (ft, 1 - ft);
    }

    [GeneratedRegex(@"\d|\b(один|одна|два|дві|три|чотири|пʼять|п'ять|пять|десять|сто|тисяч\w*|грн|гривен\w*|гривн\w*|uah|usd|eur|км|km)\b|[₴$€%]",
        RegexOptions.IgnoreCase)]
    private static partial Regex NumberRegex();
}

/// <summary>
/// Domain terms and category keywords. Built from a manual list plus the stems of all
/// knowledge-base section titles, so new documents extend the dictionary automatically.
/// </summary>
public sealed class DomainVocabulary
{
    /// <summary>Keywords that indicate a category (stemmed at construction).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> CategoryKeywords = new Dictionary<string, string[]>
    {
        ["documents"] = ["документи", "паспорт", "посвідчення", "права", "вік", "років", "стаж", "міжнародне", "іноземець", "нерезидент", "license", "age"],
        ["deposit"] = ["депозит", "застава", "блокування", "заблокувати", "оплата", "оплатити", "картка", "готівка", "передоплата", "deposit", "payment"],
        ["insurance"] = ["страхування", "страховка", "франшиза", "каско", "осцпв", "поліс", "захист", "insurance"],
        ["cancellation"] = ["скасування", "скасувати", "відмінити", "анулювати", "неявка", "повернення коштів", "cancel", "refund"],
        ["fuel"] = ["пальне", "паливо", "бензин", "дизель", "бак", "заправити", "заправка", "fuel"],
        ["mileage"] = ["пробіг", "кілометр", "км", "кілометраж", "ліміт", "mileage"],
        ["late-return"] = ["запізнення", "запізнитися", "пізніше", "затримка", "прострочення", "продовжити", "продовження", "дострокове", "late"],
        ["damage"] = ["пошкодження", "подряпина", "вмятина", "дтп", "аварія", "ремонт", "поліція", "європротокол", "розбити", "damage", "accident"],
        ["borders"] = ["кордон", "закордон", "виїзд", "виїхати", "польща", "європа", "молдова", "зелена карта", "abroad", "border"],
        ["extras"] = ["крісло", "дитяче", "бустер", "другий водій", "навігатор", "ланцюги", "бокс", "роутер", "додаткові", "послуги", "доставка", "extras"],
        ["pickup"] = ["отримання", "забрати", "видача", "офіс", "графік", "години роботи", "огляд", "мийка", "прибирання", "куріння", "тварини", "pickup"],
        ["booking"] = ["бронювання", "забронювати", "бронь", "підтвердження", "адміністратор", "наперед", "активні", "booking"],
        ["children"] = ["дитина", "діти", "немовля", "isofix", "ізофікс", "візок", "child", "kids", "baby"],
        ["ev"] = ["електромобіль", "електрокар", "зарядка", "зарядити", "запас ходу", "зарядна станція", "розетка", "батарея", "electric", "charging"],
        ["winter"] = ["зима", "зимова", "сніг", "мороз", "ожеледиця", "лід", "гори", "карпати", "буковель", "winter", "snow"],
        ["roadside"] = ["евакуатор", "прокол", "колесо", "акумулятор", "зламалася", "поломка", "несправність", "допомога на дорозі", "breakdown"],
        ["fines"] = ["штраф", "камера", "паркування", "парковка", "евакуювали", "віньєтка", "платна дорога", "fine", "parking", "vignette"],
        ["long-term"] = ["довгострокова", "місяць", "місяці", "півроку", "корпоративний", "співробітники", "long-term", "corporate"],
        ["support"] = ["скарга", "підтримка", "контакти", "зв'язатися", "email", "відгук", "забуті речі", "support", "complaint"],
        ["rules"] = ["таксі", "uber", "bolt", "перегони", "буксирування", "бездоріжжя", "пасажири", "вантаж", "taxi"],
        ["cars"] = ["категорія", "клас", "модель", "аналог", "коробка передач", "механіка", "мінівен", "седан", "хетчбек", "car category"],
        ["technical"] = ["тиск", "шини", "мастило", "контрольна лампа", "датчик", "камера заднього огляду", "bluetooth", "carplay", "мультимедіа", "круїз"],
        ["privacy"] = ["персональні дані", "gps", "трекер", "стежите", "конфіденційність", "обліковий запис", "акаунт", "privacy", "tracking"],
        ["airport"] = ["аеропорт", "рейс", "вокзал", "бориспіль", "термінал", "airport", "flight"]
    };

    private static readonly string[] ManualDomainTerms =
    [
        "депозит", "франшиза", "каско", "осцпв", "стаж", "пробіг", "штраф", "пеня", "страхування", "дтп", "європротокол",
        "зелена карта", "бустер", "передоплата", "тариф", "кілометраж", "неявка", "пальне", "запізнення", "кордон"
    ];

    /// <summary>Category → keyword phrases, each phrase as a list of stems.</summary>
    private readonly Dictionary<string, string[][]> _categoryPhrases;
    private readonly HashSet<string> _domainStems;

    public DomainVocabulary(IEnumerable<string>? sectionTitles = null)
    {
        _categoryPhrases = CategoryKeywords.ToDictionary(
            kv => kv.Key,
            kv => kv.Value
                .Select(phrase => TextNormalizer.ContentStems(phrase).ToArray())
                .Where(stems => stems.Length > 0)
                .ToArray());

        _domainStems = ManualDomainTerms
            .Concat(CategoryKeywords.Values.SelectMany(v => v))
            .Concat(sectionTitles ?? [])
            .SelectMany(TextNormalizer.ContentStems)
            .Where(s => s.Length >= 3 && !s.All(char.IsDigit))
            .ToHashSet();
    }

    public bool IsDomainTerm(string stem) => _domainStems.Any(d => TextNormalizer.StemsMatch(stem, d));

    /// <summary>
    /// Category with the most matched keyword phrases (a multi-word phrase counts only when all of
    /// its words occur); <c>null</c> when there are no matches or a tie.
    /// </summary>
    public string? PredictCategory(IReadOnlyList<string> stems)
    {
        var scores = _categoryPhrases
            .Select(kv => (Category: kv.Key, Hits: kv.Value.Count(phrase =>
                phrase.All(p => stems.Any(s => TextNormalizer.StemsMatch(s, p))))))
            .Where(x => x.Hits > 0)
            .OrderByDescending(x => x.Hits)
            .ToList();

        if (scores.Count == 0) return null;
        if (scores.Count > 1 && scores[0].Hits == scores[1].Hits) return null;
        return scores[0].Category;
    }
}
