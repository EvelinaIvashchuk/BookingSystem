using BookingSystem.Ai.Tools;
using Microsoft.Extensions.AI;

namespace BookingSystem.Ai.Slots;

/// <summary>
/// Slot filling around a single tool call (contribution of the work):
/// <list type="number">
/// <item>merge: arguments the model omitted are taken from the slots known in this conversation;
///       a filter set to "any" clears the corresponding slot;</item>
/// <item>validate and normalize the merged arguments (<see cref="ISlotValidator"/>);</item>
/// <item>if something is missing or invalid the tool is NOT executed — the model receives
///       <c>invalid_slots</c> and asks the user a clarifying question;</item>
/// <item>after a successful call the slots are updated (<see cref="Commit"/>).</item>
/// </list>
/// </summary>
public sealed class SlotFillingEngine(ISlotValidator validator)
{
    public async Task<SlotFillingDecision> PrepareAsync(
        string toolName, AIFunctionArguments arguments, AgentTurnContext turn, CancellationToken ct)
    {
        var spec = SlotSchema.For(toolName);
        if (spec is null)
            return SlotFillingDecision.NotApplicable;

        var provided = arguments
            .Where(a => !SlotValues.IsEmpty(a.Value))
            .Select(a => a.Key)
            .ToHashSet();

        // "any" removes a previously known filter.
        var cleared = new List<string>();
        foreach (var argument in spec.Arguments.Keys.Where(spec.IsFilter).ToList())
        {
            if (arguments.TryGetValue(argument, out var value) && SlotValues.IsAny(value))
            {
                arguments.Remove(argument);
                provided.Remove(argument);
                cleared.Add(spec.Arguments[argument]);
            }
        }

        // Merge what the user said earlier but the model left out.
        var merged = new List<string>();
        foreach (var (argument, slot) in spec.Arguments)
        {
            if (provided.Contains(argument) || cleared.Contains(slot)) continue;
            if (turn.Slots.Get(slot) is { } known)
            {
                arguments[argument] = known;
                merged.Add(slot);
            }
        }

        var result = await validator.ValidateAsync(
            toolName,
            new Dictionary<string, object?>(arguments),
            turn.ValidationContext(provided),
            ct);

        if (result.IsValid)
        {
            // The tool receives canonical values (YYYY-MM-DD, enum names, numbers).
            foreach (var key in arguments.Keys.Where(k => !result.NormalizedArguments.ContainsKey(k)).ToList())
                arguments.Remove(key);
            foreach (var (key, value) in result.NormalizedArguments)
                arguments[key] = value;
        }

        return new SlotFillingDecision(result, merged, cleared);
    }

    /// <summary>Stores the values a successful call actually used.</summary>
    public static void Commit(string toolName, SlotFillingDecision decision, BookingSlots slots)
    {
        var spec = SlotSchema.For(toolName);
        if (spec is null || decision.Validation is null) return;

        foreach (var (argument, slot) in spec.Arguments)
            if (decision.Validation.NormalizedArguments.TryGetValue(argument, out var value) && !SlotValues.IsEmpty(value))
                slots.Set(slot, value);

        foreach (var slot in decision.ClearedSlots)
            slots.Set(slot, null);
    }

    /// <summary>The structured error returned to the model instead of running the tool.</summary>
    public static ToolError ToToolError(SlotValidationResult result)
    {
        var parts = result.Missing.Select(Describe)
            .Concat(result.Invalid.Select(i => Describe(i.Split(':')[0])))
            .Distinct();

        return new ToolError(
            ToolErrorCodes.InvalidSlots,
            "The tool was NOT executed: booking parameters are missing or invalid. Do not guess values. " +
            "If a date contradicts the user's words, recompute it from the calendar; otherwise ask the user " +
            $"ONE short question about: {string.Join(", ", parts)}.")
        {
            Missing = result.Missing,
            Invalid = result.Invalid
        };
    }

    private static string Describe(string slot) => slot switch
    {
        SlotNames.PickupDate => "pickup date",
        SlotNames.ReturnDate => "return date",
        SlotNames.SelectedCarId => "which car",
        SlotNames.MinSeats => "number of seats",
        SlotNames.MaxPricePerDay => "budget per day",
        SlotNames.Transmission => "transmission",
        SlotNames.FuelType => "fuel type",
        SlotNames.Category => "car category",
        SlotNames.Location => "city",
        _ => slot
    };
}

/// <param name="Validation"><c>null</c> when the tool has no slots.</param>
/// <param name="MergedSlots">Slots whose values were filled in from the conversation state.</param>
/// <param name="ClearedSlots">Filters the model explicitly dropped with "any".</param>
public sealed record SlotFillingDecision(
    SlotValidationResult? Validation,
    IReadOnlyList<string> MergedSlots,
    IReadOnlyList<string> ClearedSlots)
{
    public static readonly SlotFillingDecision NotApplicable = new(null, [], []);

    public bool CanExecute => Validation is null || Validation.IsValid;
}
