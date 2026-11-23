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

    public AmountCheckOptions AmountCheck { get; set; } = new();

    public CarRankingOptions CarRanking { get; set; } = new();

    public RateLimitOptions RateLimit { get; set; } = new();

    public KnowledgeBaseOptions KnowledgeBase { get; set; } = new();

    public SearchOptions Search { get; set; } = new();

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

    /// <summary>
    /// Block a call whose date the user never stated (the model invented it) and ask for it instead.
    /// Applies only while slot filling is enabled (and in shadow validation for metrics).
    /// </summary>
    public bool RequireGroundedDates { get; set; } = true;
}

/// <summary>
/// Check of amounts of money in the agent's reply (<c>Ai:AmountCheck</c>): every amount must come from a tool
/// result or from the conversation; sentences with any other amount (an applied "discount", a made-up price) are removed.
/// </summary>
public sealed class AmountCheckOptions
{
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Ranking of available cars by the user's free-text wishes (<c>Ai:CarRanking</c>) with the model trained by
/// <c>tools/ranking/train.py</c>. Paths are relative to the content root (or the application folder).
/// </summary>
public sealed class CarRankingOptions
{
    public bool Enabled { get; set; } = true;
    public string ModelPath { get; set; } = "CarRanking/car_ranker.json";
    public string SpecsPath { get; set; } = "CarRanking/car_specs.json";
}

/// <summary>Limits for <c>POST /Ai/Chat</c> (<c>Ai:RateLimit</c>).</summary>
public sealed class RateLimitOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Requests per minute from one IP address.</summary>
    public int PerIpPerMinute { get; set; } = 20;

    /// <summary>Requests per hour for one user (signed in) or chat session (anonymous).</summary>
    public int PerSessionPerHour { get; set; } = 60;
}

public sealed class KnowledgeBaseOptions
{
    /// <summary>Folder with markdown files, relative to the content root.</summary>
    public string Path { get; set; } = "KnowledgeBase";

    /// <summary>Index changed files in the background when the application starts.</summary>
    public bool IndexOnStartup { get; set; } = true;

    /// <summary>Sections longer than this (approx. tokens) are split by paragraphs.</summary>
    public int MaxChunkTokens { get; set; } = 350;

    public int EmbeddingBatchSize { get; set; } = 50;
}

/// <summary>Retrieval settings (<c>Ai:Search</c>).</summary>
public sealed class SearchOptions
{
    /// <summary>Method used by the <c>search_rental_policies</c> tool.</summary>
    public KnowledgeSearchMode Mode { get; set; } = KnowledgeSearchMode.HybridAdaptive;

    /// <summary>Fragments returned to the agent.</summary>
    public int TopK { get; set; } = 3;

    /// <summary>Candidates taken from each method before fusion.</summary>
    public int Candidates { get; set; } = 20;

    /// <summary>RRF constant k.</summary>
    public int RrfK { get; set; } = 60;

    public AdaptiveFusionOptions Adaptive { get; set; } = new();
}

/// <summary>
/// Parameters of the adaptive fusion:
/// w_ft = clamp(A + B·d − C·max(0, n − N0) + E·num, MinWeight, MaxWeight), w_vec = 1 − w_ft;
/// score = (w_vec/(k + r_vec) + w_ft/(k + r_ft)) · (1 + Beta·[category matches]).
/// Tuned on the dev split only.
/// </summary>
public sealed class AdaptiveFusionOptions
{
    public double A { get; set; } = 0.45;
    public double B { get; set; } = 0.40;
    public double C { get; set; } = 0.05;
    public int N0 { get; set; } = 4;
    public double E { get; set; } = 0.10;
    public double Beta { get; set; } = 0.25;
    public double MinWeight { get; set; } = 0.15;
    public double MaxWeight { get; set; } = 0.85;
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
