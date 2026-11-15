using System.Globalization;
using System.Text.Json;

namespace BookingSystem.Ai.Slots;

/// <summary>
/// Reads argument values produced by a model. They may arrive as <see cref="JsonElement"/>
/// (real providers), CLR primitives (tests) or numbers written as strings.
/// </summary>
public static class SlotValues
{
    public const string DateFormat = "yyyy-MM-dd";

    private static readonly HashSet<string> AnyValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "any", "none", "no preference", "doesn't matter", "*",
        "будь-яка", "будь-який", "будь-яке", "будь яка", "будь який", "неважливо", "не важливо", "без різниці", "байдуже"
    };

    public static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>True when the value is absent: null, JSON null or an empty string.</summary>
    public static bool IsEmpty(object? value) => value switch
    {
        null => true,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => true,
        _ => string.IsNullOrWhiteSpace(AsString(value))
    };

    /// <summary>True when the model explicitly asks to drop a filter ("any", "будь-яка"...).</summary>
    public static bool IsAny(object? value) => AsString(value) is { } s && AnyValues.Contains(s.Trim());

    public static string? AsString(object? value) => value switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() is { } s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null,
        JsonElement { ValueKind: JsonValueKind.Number } e => e.GetRawText(),
        JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } e => e.GetRawText(),
        JsonElement => null,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    public static bool TryGetInt(object? value, out int result)
    {
        result = 0;
        switch (value)
        {
            case int i: result = i; return true;
            case long l when l is >= int.MinValue and <= int.MaxValue: result = (int)l; return true;
            case JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetInt32(out result): return true;
        }
        return int.TryParse(AsString(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    public static bool TryGetDecimal(object? value, out decimal result)
    {
        result = 0;
        switch (value)
        {
            case decimal d: result = d; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case double db: result = (decimal)db; return true;
            case JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetDecimal(out result): return true;
        }
        var text = AsString(value)?.Replace(',', '.').Replace(" ", "");
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>Accepts YYYY-MM-DD and, leniently, ISO date-times (their calendar date is used).</summary>
    public static bool TryParseDate(object? value, out DateOnly date)
    {
        date = default;
        var text = AsString(value);
        if (text is null) return false;

        if (DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            date = DateOnly.FromDateTime(dto.Date);
            return true;
        }

        return false;
    }
}
