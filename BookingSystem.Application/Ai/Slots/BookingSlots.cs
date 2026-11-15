using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookingSystem.Enums;

namespace BookingSystem.Ai.Slots;

/// <summary>Names of booking slots (also used in <c>invalid_slots</c> errors and experiment datasets).</summary>
public static class SlotNames
{
    public const string PickupDate = nameof(BookingSlots.PickupDate);
    public const string ReturnDate = nameof(BookingSlots.ReturnDate);
    public const string Location = nameof(BookingSlots.Location);
    public const string Category = nameof(BookingSlots.Category);
    public const string Transmission = nameof(BookingSlots.Transmission);
    public const string FuelType = nameof(BookingSlots.FuelType);
    public const string MinSeats = nameof(BookingSlots.MinSeats);
    public const string MaxPricePerDay = nameof(BookingSlots.MaxPricePerDay);
    public const string SelectedCarId = nameof(BookingSlots.SelectedCarId);
}

/// <summary>
/// Booking parameters collected during a conversation (slot filling state).
/// Persisted in <c>ChatSession.SlotsJson</c> and shown to the model as "known parameters".
/// </summary>
public sealed class BookingSlots
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public DateOnly? PickupDate { get; set; }
    public DateOnly? ReturnDate { get; set; }
    public string? Location { get; set; }
    public string? Category { get; set; }
    public Transmission? Transmission { get; set; }
    public FuelType? FuelType { get; set; }
    public int? MinSeats { get; set; }
    public decimal? MaxPricePerDay { get; set; }
    public int? SelectedCarId { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Describe().Count == 0;

    public static BookingSlots FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new BookingSlots();
        try
        {
            return JsonSerializer.Deserialize<BookingSlots>(json, JsonOptions) ?? new BookingSlots();
        }
        catch (JsonException)
        {
            return new BookingSlots();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Known values as (slot, text) pairs, in a stable order.</summary>
    public IReadOnlyList<(string Slot, string Value)> Describe()
    {
        var list = new List<(string, string)>();
        if (PickupDate is { } p) list.Add((SlotNames.PickupDate, SlotValues.FormatDate(p)));
        if (ReturnDate is { } r) list.Add((SlotNames.ReturnDate, SlotValues.FormatDate(r)));
        if (Location is { } l) list.Add((SlotNames.Location, l));
        if (Category is { } c) list.Add((SlotNames.Category, c));
        if (Transmission is { } t) list.Add((SlotNames.Transmission, t.ToString()));
        if (FuelType is { } f) list.Add((SlotNames.FuelType, f.ToString()));
        if (MinSeats is { } s) list.Add((SlotNames.MinSeats, s.ToString(CultureInfo.InvariantCulture)));
        if (MaxPricePerDay is { } m) list.Add((SlotNames.MaxPricePerDay, m.ToString("0.##", CultureInfo.InvariantCulture)));
        if (SelectedCarId is { } id) list.Add((SlotNames.SelectedCarId, id.ToString(CultureInfo.InvariantCulture)));
        return list;
    }

    /// <summary>Returns the slot value in the form a tool argument expects (string/int/decimal), or null.</summary>
    public object? Get(string slot) => slot switch
    {
        SlotNames.PickupDate => PickupDate is { } p ? SlotValues.FormatDate(p) : null,
        SlotNames.ReturnDate => ReturnDate is { } r ? SlotValues.FormatDate(r) : null,
        SlotNames.Location => Location,
        SlotNames.Category => Category,
        SlotNames.Transmission => Transmission?.ToString(),
        SlotNames.FuelType => FuelType?.ToString(),
        SlotNames.MinSeats => MinSeats,
        SlotNames.MaxPricePerDay => MaxPricePerDay,
        SlotNames.SelectedCarId => SelectedCarId,
        _ => null
    };

    /// <summary>Stores an already validated and normalized value; <c>null</c> clears the slot.</summary>
    public void Set(string slot, object? value)
    {
        switch (slot)
        {
            case SlotNames.PickupDate: PickupDate = SlotValues.TryParseDate(value, out var p) ? p : null; break;
            case SlotNames.ReturnDate: ReturnDate = SlotValues.TryParseDate(value, out var r) ? r : null; break;
            case SlotNames.Location: Location = SlotValues.AsString(value); break;
            case SlotNames.Category: Category = SlotValues.AsString(value); break;
            case SlotNames.Transmission: Transmission = BookingVocabulary.TryParseTransmission(SlotValues.AsString(value), out var t) ? t : null; break;
            case SlotNames.FuelType: FuelType = BookingVocabulary.TryParseFuelType(SlotValues.AsString(value), out var f) ? f : null; break;
            case SlotNames.MinSeats: MinSeats = SlotValues.TryGetInt(value, out var s) ? s : null; break;
            case SlotNames.MaxPricePerDay: MaxPricePerDay = SlotValues.TryGetDecimal(value, out var m) ? m : null; break;
            case SlotNames.SelectedCarId: SelectedCarId = SlotValues.TryGetInt(value, out var id) ? id : null; break;
        }
    }
}
