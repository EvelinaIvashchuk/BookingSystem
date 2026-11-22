using System.Globalization;
using BookingSystem.Data;
using BookingSystem.Enums;
using BookingSystem.Services;
using static BookingSystem.Ai.Slots.SlotSchema;

namespace BookingSystem.Ai.Slots;

/// <summary>
/// Checks tool arguments before execution and normalizes them to canonical values.
/// Every problem is reported as <c>"Slot: explanation"</c> so the model can ask a precise question.
/// Rules:
/// <list type="bullet">
/// <item>required arguments of the tool are present;</item>
/// <item>dates parse, pickup is not in the past and at most <see cref="RentalRules.MaxAdvanceBookingDays"/> ahead,
///       return is after pickup, duration within <see cref="RentalRules.MaxRentalDays"/> days;</item>
/// <item>dates agree with weekdays / duration the user stated in the current message;</item>
/// <item>optionally, every date the model provided is grounded in the conversation (<see cref="DateGrounding"/>),
///       otherwise it is reported as missing;</item>
/// <item>MinSeats 1–9, MaxPricePerDay &gt; 0, enum synonyms map to known values;</item>
/// <item>the car exists (and is Available when booking).</item>
/// </list>
/// </summary>
public sealed class SlotValidator(IUnitOfWork uow) : ISlotValidator
{
    public const int MinSeatsLimit = 1;
    public const int MaxSeatsLimit = 9;

    public async Task<SlotValidationResult> ValidateAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        SlotValidationContext context,
        CancellationToken ct = default)
    {
        var normalized = new Dictionary<string, object?>(arguments);
        var spec = For(toolName);
        if (spec is null)
            return new SlotValidationResult(normalized, [], []);

        var missing = new List<string>();
        var invalid = new List<string>();

        foreach (var argument in spec.RequiredArguments)
            if (!arguments.TryGetValue(argument, out var value) || SlotValues.IsEmpty(value))
                missing.Add(spec.Arguments[argument]);

        // Filters explicitly set to "any" are simply dropped.
        foreach (var argument in spec.Arguments.Keys.Where(spec.IsFilter))
            if (arguments.TryGetValue(argument, out var value) && (SlotValues.IsEmpty(value) || SlotValues.IsAny(value)))
                normalized.Remove(argument);

        ValidateDates(spec, arguments, normalized, context, missing, invalid);
        ValidateFilters(spec, arguments, normalized, invalid);
        await ValidateCarAsync(spec, arguments, normalized, invalid);

        return new SlotValidationResult(normalized, missing, invalid);
    }

    // ── Dates ────────────────────────────────────────────────────────────────

    private static void ValidateDates(
        ToolSlotSpec spec,
        IReadOnlyDictionary<string, object?> arguments,
        Dictionary<string, object?> normalized,
        SlotValidationContext context,
        List<string> missing,
        List<string> invalid)
    {
        if (!spec.Arguments.ContainsKey(PickupDateArg)) return;

        var pickup = ParseDate(PickupDateArg, SlotNames.PickupDate, arguments, normalized, invalid);
        var ret = ParseDate(ReturnDateArg, SlotNames.ReturnDate, arguments, normalized, invalid);
        var errorsBefore = invalid.Count;
        var today = context.Today;

        if (pickup is { } p)
        {
            if (p < today)
                invalid.Add($"{SlotNames.PickupDate}: {Format(p)} is in the past (today is {Format(today)}).");
            else if (p > today.AddDays(RentalRules.MaxAdvanceBookingDays))
                invalid.Add($"{SlotNames.PickupDate}: {Format(p)} is more than {RentalRules.MaxAdvanceBookingDays} days ahead (latest {Format(today.AddDays(RentalRules.MaxAdvanceBookingDays))}).");
        }

        if (pickup is { } start && ret is { } end)
        {
            var days = end.DayNumber - start.DayNumber;
            if (days < RentalRules.MinRentalDays)
                invalid.Add($"{SlotNames.ReturnDate}: {Format(end)} must be after {SlotNames.PickupDate} {Format(start)}.");
            else if (days > RentalRules.MaxRentalDays)
                invalid.Add($"{SlotNames.ReturnDate}: a rental of {days} days exceeds the maximum of {RentalRules.MaxRentalDays} days.");
        }

        // Consistency with the user's own words, only when the dates themselves are fine.
        if (invalid.Count == errorsBefore)
            CheckAgainstUserText(pickup, ret, context, invalid);

        // A date the user never stated is treated as missing: the model must ask instead of guessing.
        if (invalid.Count == errorsBefore && context.RequireGroundedDates)
        {
            if (pickup is { } pickupValue && !IsGrounded(PickupDateArg, pickupValue, isReturn: false, context))
                missing.Add(SlotNames.PickupDate);
            if (ret is { } returnValue && !IsGrounded(ReturnDateArg, returnValue, isReturn: true, context))
                missing.Add(SlotNames.ReturnDate);
        }
    }

    private static bool IsGrounded(string argument, DateOnly date, bool isReturn, SlotValidationContext context)
    {
        // Values merged from the session state were grounded when they were stored.
        if (!context.ModelProvidedArguments.Contains(argument)) return true;

        var userMessages = context.LatestUserMessage is { } latest
            ? [.. context.EarlierUserMessages, latest]
            : context.EarlierUserMessages;
        var known = isReturn ? context.KnownSlots?.ReturnDate : context.KnownSlots?.PickupDate;

        return DateGrounding.IsGrounded(date, isReturn,
            new DateGroundingContext(context.Today, userMessages, context.PreviousAssistantMessage, known));
    }

    private static void CheckAgainstUserText(DateOnly? pickup, DateOnly? ret, SlotValidationContext context, List<string> invalid)
    {
        var provided = new List<(string Slot, DateOnly Date)>();
        if (pickup is { } p && context.ModelProvidedArguments.Contains(PickupDateArg)) provided.Add((SlotNames.PickupDate, p));
        if (ret is { } r && context.ModelProvidedArguments.Contains(ReturnDateArg)) provided.Add((SlotNames.ReturnDate, r));
        if (provided.Count == 0) return;

        var weekdays = DateExpressionHints.MentionedWeekdays(context.LatestUserMessage);
        if (weekdays.Count > 0 && !provided.Any(d => weekdays.Contains(d.Date.DayOfWeek)))
        {
            var (slot, date) = provided[0];
            invalid.Add(
                $"{slot}: {Format(date)} is a {date.DayOfWeek}, but the user mentioned {string.Join("/", weekdays)}. " +
                "Recompute the date from the calendar in your instructions.");
        }

        if (pickup is { } start && ret is { } end
            && DateExpressionHints.MentionedDurationDays(context.LatestUserMessage) is { } duration
            && end.DayNumber - start.DayNumber != duration)
        {
            invalid.Add(
                $"{SlotNames.ReturnDate}: {Format(start)} → {Format(end)} is {end.DayNumber - start.DayNumber} days, " +
                $"but the user asked for {duration} days (return date should be {Format(start.AddDays(duration))}).");
        }
    }

    private static DateOnly? ParseDate(
        string argument, string slot,
        IReadOnlyDictionary<string, object?> arguments,
        Dictionary<string, object?> normalized,
        List<string> invalid)
    {
        if (!arguments.TryGetValue(argument, out var value) || SlotValues.IsEmpty(value))
            return null;

        if (SlotValues.TryParseDate(value, out var date))
        {
            normalized[argument] = Format(date);
            return date;
        }

        invalid.Add($"{slot}: '{SlotValues.AsString(value)}' is not a date; use YYYY-MM-DD.");
        return null;
    }

    // ── Filters ──────────────────────────────────────────────────────────────

    private static void ValidateFilters(
        ToolSlotSpec spec,
        IReadOnlyDictionary<string, object?> arguments,
        Dictionary<string, object?> normalized,
        List<string> invalid)
    {
        string? Present(string argument) =>
            spec.Arguments.ContainsKey(argument) && normalized.ContainsKey(argument)
                ? SlotValues.AsString(arguments[argument])
                : null;

        if (Present(MinSeatsArg) is { } seats)
        {
            if (SlotValues.TryGetInt(arguments[MinSeatsArg], out var n) && n is >= MinSeatsLimit and <= MaxSeatsLimit)
                normalized[MinSeatsArg] = n;
            else
                invalid.Add($"{SlotNames.MinSeats}: '{seats}' must be a whole number from {MinSeatsLimit} to {MaxSeatsLimit}.");
        }

        if (Present(MaxPricePerDayArg) is { } price)
        {
            if (SlotValues.TryGetDecimal(arguments[MaxPricePerDayArg], out var m) && m > 0)
                normalized[MaxPricePerDayArg] = m;
            else
                invalid.Add($"{SlotNames.MaxPricePerDay}: '{price}' must be a positive amount in UAH.");
        }

        if (Present(TransmissionArg) is { } transmission)
        {
            if (BookingVocabulary.TryParseTransmission(transmission, out var t))
                normalized[TransmissionArg] = t.ToString();
            else
                invalid.Add($"{SlotNames.Transmission}: unknown value '{transmission}'. Allowed: {string.Join(", ", BookingVocabulary.TransmissionValues)}.");
        }

        if (Present(FuelTypeArg) is { } fuel)
        {
            if (BookingVocabulary.TryParseFuelType(fuel, out var f))
                normalized[FuelTypeArg] = f.ToString();
            else
                invalid.Add($"{SlotNames.FuelType}: unknown value '{fuel}'. Allowed: {string.Join(", ", BookingVocabulary.FuelTypeValues)}.");
        }

        if (Present(CategoryArg) is { } category)
        {
            if (BookingVocabulary.TryNormalizeCategory(category, out var c))
                normalized[CategoryArg] = c;
            else
                invalid.Add($"{SlotNames.Category}: unknown value '{category}'. Allowed: {string.Join(", ", BookingVocabulary.CategoryValues)}.");
        }

        if (Present(LocationArg) is { } location)
            normalized[LocationArg] = BookingVocabulary.NormalizeCity(location);
    }

    // ── Car ──────────────────────────────────────────────────────────────────

    private async Task ValidateCarAsync(
        ToolSlotSpec spec,
        IReadOnlyDictionary<string, object?> arguments,
        Dictionary<string, object?> normalized,
        List<string> invalid)
    {
        if (!spec.Arguments.ContainsKey(CarIdArg)
            || !arguments.TryGetValue(CarIdArg, out var value)
            || SlotValues.IsEmpty(value))
            return;

        if (!SlotValues.TryGetInt(value, out var carId))
        {
            invalid.Add($"{SlotNames.SelectedCarId}: '{SlotValues.AsString(value)}' is not a car id from search results.");
            return;
        }

        var car = await uow.Cars.GetWithCategoryAsync(carId);
        if (car is null)
        {
            invalid.Add($"{SlotNames.SelectedCarId}: car {carId} does not exist. Use an id from search results.");
            return;
        }

        if (spec.RequiresAvailableCar && car.Status != CarStatus.Available)
        {
            invalid.Add($"{SlotNames.SelectedCarId}: {car.FullName} is {car.Status} and cannot be booked.");
            return;
        }

        normalized[CarIdArg] = carId;
    }

    private static string Format(DateOnly date) => date.ToString(SlotValues.DateFormat, CultureInfo.InvariantCulture);
}
