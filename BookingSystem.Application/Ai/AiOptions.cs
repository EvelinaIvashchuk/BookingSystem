namespace BookingSystem.Ai;

/// <summary>
/// Strongly typed view of the <c>Ai</c> configuration section.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Model in <c>provider:model</c> form, e.g. <c>groq:openai/gpt-oss-120b</c>.</summary>
    public string DefaultModel { get; set; } = "groq:openai/gpt-oss-120b";

    /// <summary>Upper bound on model calls inside one agent turn (tool-calling loop).</summary>
    public int MaxAgentIterations { get; set; } = 5;

    public float Temperature { get; set; } = 0.2f;

    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>How many previous session messages are passed to the model.</summary>
    public int HistoryLength { get; set; } = 12;

    /// <summary>Maximum length of a single user message.</summary>
    public int MaxMessageLength { get; set; } = 1000;

    /// <summary>How long a booking draft prepared by the agent can be confirmed.</summary>
    public int PendingActionTtlMinutes { get; set; } = 15;

    /// <summary>IANA time zone used to tell the model what "today" is.</summary>
    public string TimeZone { get; set; } = "Europe/Kyiv";

    public Dictionary<string, AiProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public AiEmbeddingOptions Embedding { get; set; } = new();

    public SlotFillingOptions SlotFilling { get; set; } = new();

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

/// <summary>Ablation switch for the slot filling algorithm (<c>Ai:SlotFilling:Enabled</c>).</summary>
public sealed class SlotFillingOptions
{
    /// <summary>
    /// When false, tools run with the model's arguments as is: no merge with known slots,
    /// no pre-validation, no known-parameters block in the prompt (baseline for experiments).
    /// Arguments are still validated in shadow mode for logging.
    /// </summary>
    public bool Enabled { get; set; } = true;
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
