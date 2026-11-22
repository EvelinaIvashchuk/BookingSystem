using System.Globalization;
using System.Text.RegularExpressions;

namespace BookingSystem.Ai.Knowledge;

/// <summary>
/// Normalization for full-text search over Ukrainian text: lower-casing, tokenization,
/// stop-word removal and a light suffix-stripping stemmer. MySQL FULLTEXT has no Ukrainian
/// morphology, so "депозит", "депозиту", "депозитом" would be different words without it.
/// The same normalization is applied to indexed chunks and to queries.
/// </summary>
public static partial class TextNormalizer
{
    private const int MinStemLength = 3;

    /// <summary>
    /// Endings removed by the stemmer, longest first (one pass). Infinitive endings (-ити, -ати, -ти)
    /// are deliberately absent: they would cut noun plurals ("депозити" → "депоз").
    /// </summary>
    private static readonly string[] Endings =
    [
        "еннями", "аннями", "ування", "ювання", "ованих", "ованої", "ованим",
        "енням", "анням", "ового", "овому", "ості", "остю", "ість",
        "ення", "ання", "яння", "енні", "анні", "енню", "анню", "ться",
        "ами", "ями", "ові", "еві", "єві", "ого", "ому", "ими", "іми", "ень", "ань", "ють", "уть", "ать", "ять",
        "их", "іх", "ий", "ій", "ої", "ою", "ею", "єю", "ах", "ях", "ів", "їв", "ям", "ам", "ом", "ем", "єм", "ся", "ть", "ує",
        "у", "ю", "а", "я", "і", "ї", "и", "е", "є", "о", "ь", "s"
    ];

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // Ukrainian
        "і", "й", "та", "а", "але", "або", "чи", "що", "як", "це", "цей", "ця", "ці", "цього", "цієї", "той", "ті", "тому",
        "у", "в", "на", "з", "із", "зі", "до", "від", "по", "за", "для", "про", "під", "над", "при", "без", "через", "між",
        "о", "об", "не", "ні", "так", "же", "ж", "б", "би", "бо", "якщо", "коли", "де", "куди", "хто", "який", "яка", "яке",
        "які", "якого", "якій", "яку", "яким", "яких", "мені", "мене", "мій", "моя", "моє", "мої", "я", "ти", "ви", "вам",
        "вас", "ваш", "ваша", "ваше", "ваші", "ми", "нам", "нас", "наш", "наша", "наше", "наші", "він", "вона", "воно",
        "вони", "їх", "його", "її", "є", "буде", "будуть", "був", "була", "було", "були", "бути", "можна", "треба",
        "потрібно", "маю", "має", "мають", "ще", "вже", "також", "тільки", "лише", "дуже", "теж", "все", "усі", "всі",
        "будь", "ласка", "скільки", "чому", "щодо", "саме", "тобто", "ось", "там", "тут", "тоді", "зараз", "хочу",
        "можу", "можемо", "мене", "себе", "свій", "своє", "свою", "свої", "чим", "ніж", "щоб", "аби",
        // English
        "the", "a", "an", "of", "to", "in", "on", "for", "and", "or", "is", "are", "what", "how", "can", "i", "my",
        "me", "you", "your", "do", "does", "if", "with", "at", "by", "be", "it", "this", "that", "much", "many", "will"
    };

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’ʼ`][\p{L}\p{N}]+)*")]
    private static partial Regex WordRegex();

    /// <summary>Lower-cased words with apostrophes removed (п'ятниця → пятниця).</summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return WordRegex().Matches(text)
            .Select(m => m.Value.ToLower(CultureInfo.InvariantCulture)
                .Replace("'", "").Replace("’", "").Replace("ʼ", "").Replace("`", ""))
            .ToList();
    }

    public static bool IsStopWord(string token) => StopWords.Contains(token);

    public static string Stem(string token)
    {
        if (token.Length <= MinStemLength || token.All(char.IsDigit))
            return token;

        foreach (var ending in Endings)
            if (token.Length - ending.Length >= MinStemLength && token.EndsWith(ending, StringComparison.Ordinal))
                return token[..^ending.Length];

        return token;
    }

    /// <summary>Stems of all non-stop-word tokens.</summary>
    public static IReadOnlyList<string> ContentStems(string? text) =>
        Tokenize(text).Where(t => !IsStopWord(t)).Select(Stem).ToList();

    /// <summary>Text stored in <c>KnowledgeChunk.SearchText</c> and sent to MATCH ... AGAINST.</summary>
    public static string ToSearchText(string? text) => string.Join(' ', ContentStems(text));

    /// <summary>
    /// Morphology-tolerant match of two stems: one is a prefix of the other
    /// (with enough letters to avoid accidental matches).
    /// </summary>
    public static bool StemsMatch(string stem, string dictionaryStem) =>
        (stem.Length >= MinStemLength && dictionaryStem.Length >= MinStemLength && stem.StartsWith(dictionaryStem, StringComparison.Ordinal))
        || (stem.Length >= 4 && dictionaryStem.StartsWith(stem, StringComparison.Ordinal));
}
