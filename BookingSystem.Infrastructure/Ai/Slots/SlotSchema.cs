using BookingSystem.Ai.Tools;

namespace BookingSystem.Ai.Slots;

/// <summary>Which tool arguments correspond to which booking slots, and which are required.</summary>
/// <param name="Arguments">Tool argument name → slot name.</param>
/// <param name="RequiredArguments">Arguments without which the tool must not run.</param>
/// <param name="RequiresAvailableCar">The selected car must have status Available (booking).</param>
public sealed record ToolSlotSpec(
    IReadOnlyDictionary<string, string> Arguments,
    IReadOnlyList<string> RequiredArguments,
    bool RequiresAvailableCar = false)
{
    public bool IsFilter(string argument) => SlotSchema.FilterArguments.Contains(argument);
}

public static class SlotSchema
{
    public const string PickupDateArg = "pickupDate";
    public const string ReturnDateArg = "returnDate";
    public const string LocationArg = "location";
    public const string CategoryArg = "category";
    public const string TransmissionArg = "transmission";
    public const string FuelTypeArg = "fuelType";
    public const string MinSeatsArg = "minSeats";
    public const string MaxPricePerDayArg = "maxPricePerDay";
    public const string CarIdArg = "carId";

    /// <summary>Optional search filters; the model may drop one by passing "any".</summary>
    public static readonly IReadOnlySet<string> FilterArguments = new HashSet<string>
    {
        LocationArg, CategoryArg, TransmissionArg, FuelTypeArg, MinSeatsArg, MaxPricePerDayArg
    };

    private static readonly Dictionary<string, string> Dates = new()
    {
        [PickupDateArg] = SlotNames.PickupDate,
        [ReturnDateArg] = SlotNames.ReturnDate
    };

    private static readonly Dictionary<string, ToolSlotSpec> Tools = new()
    {
        [BookingTools.SearchAvailableCarsName] = new(
            new Dictionary<string, string>(Dates)
            {
                [LocationArg] = SlotNames.Location,
                [CategoryArg] = SlotNames.Category,
                [TransmissionArg] = SlotNames.Transmission,
                [FuelTypeArg] = SlotNames.FuelType,
                [MinSeatsArg] = SlotNames.MinSeats,
                [MaxPricePerDayArg] = SlotNames.MaxPricePerDay
            },
            [PickupDateArg, ReturnDateArg]),

        [BookingTools.CalculatePriceName] = new(
            new Dictionary<string, string>(Dates) { [CarIdArg] = SlotNames.SelectedCarId },
            [CarIdArg, PickupDateArg, ReturnDateArg]),

        [BookingTools.CreateRentalDraftName] = new(
            new Dictionary<string, string>(Dates) { [CarIdArg] = SlotNames.SelectedCarId },
            [CarIdArg, PickupDateArg, ReturnDateArg],
            RequiresAvailableCar: true),

        [BookingTools.GetCarDetailsName] = new(
            new Dictionary<string, string> { [CarIdArg] = SlotNames.SelectedCarId },
            [CarIdArg])
    };

    /// <summary>Slot specification of a tool, or <c>null</c> if the tool has no slots (e.g. get_my_rentals).</summary>
    public static ToolSlotSpec? For(string toolName) => Tools.GetValueOrDefault(toolName);
}
