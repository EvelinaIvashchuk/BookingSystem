namespace BookingSystem.Ai.Slots;

/// <summary>
/// Validates and normalizes tool arguments before a tool is executed (slot filling, step 3).
/// </summary>
public interface ISlotValidator
{
    Task<SlotValidationResult> ValidateAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        SlotValidationContext context,
        CancellationToken ct = default);
}

/// <param name="Today">Business-local date for "past" / "in advance" rules.</param>
/// <param name="LatestUserMessage">Text of the current user turn, used for consistency checks.</param>
/// <param name="ModelProvidedArguments">
/// Arguments the model itself produced in this call (as opposed to values merged from slots).
/// Consistency checks against the user's text apply only to these.
/// </param>
public sealed record SlotValidationContext(
    DateOnly Today,
    string? LatestUserMessage,
    IReadOnlySet<string> ModelProvidedArguments);

public sealed record SlotValidationResult(
    IReadOnlyDictionary<string, object?> NormalizedArguments,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Invalid)
{
    public bool IsValid => Missing.Count == 0 && Invalid.Count == 0;
}
