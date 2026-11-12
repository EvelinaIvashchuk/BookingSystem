namespace BookingSystem.Ai;

/// <summary>
/// Strongly typed view of the <c>Ai</c> configuration section.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Model in <c>provider:model</c> form, e.g. <c>groq:llama-3.3-70b-versatile</c>.</summary>
    public string DefaultModel { get; set; } = "groq:llama-3.3-70b-versatile";

    /// <summary>Upper bound on model calls inside one agent turn (tool-calling loop).</summary>
    public int MaxAgentIterations { get; set; } = 5;

    public float Temperature { get; set; } = 0.2f;

    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>How many previous session messages are passed to the model.</summary>
    public int HistoryLength { get; set; } = 12;

    /// <summary>Maximum length of a single user message.</summary>
    public int MaxMessageLength { get; set; } = 1000;

    /// <summary>IANA time zone used to tell the model what "today" is.</summary>
    public string TimeZone { get; set; } = "Europe/Kyiv";

    public Dictionary<string, AiProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public AiEmbeddingOptions Embedding { get; set; } = new();

    /// <summary>
    /// Prices per model. A list (not a dictionary) because model ids contain ':'
    /// which is the configuration key separator in .NET.
    /// </summary>
    public List<AiModelPricing> Pricing { get; set; } = [];
}

public sealed class AiProviderOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class AiEmbeddingOptions
{
    public string Provider { get; set; } = "gemini";
    public string Model { get; set; } = "gemini-embedding-001";
}

public sealed class AiModelPricing
{
    public string Model { get; set; } = string.Empty;
    public decimal InputPer1M { get; set; }
    public decimal OutputPer1M { get; set; }
}
