namespace BookingSystem.Ai;

/// <summary>
/// Parsed <c>provider:model</c> identifier. Only the first ':' separates the provider,
/// so model names like <c>groq:openai/gpt-oss-120b</c> are preserved as is.
/// </summary>
public readonly record struct ModelRef(string Provider, string Model)
{
    public static ModelRef Parse(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new FormatException("Model id is empty. Expected 'provider:model'.");

        var separator = id.IndexOf(':');
        if (separator <= 0 || separator == id.Length - 1)
            throw new FormatException($"Model id '{id}' is invalid. Expected 'provider:model'.");

        return new ModelRef(id[..separator].Trim().ToLowerInvariant(), id[(separator + 1)..].Trim());
    }

    public override string ToString() => $"{Provider}:{Model}";
}
