using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookingSystem.Evaluation;

public sealed class ScenarioFile
{
    public string Now { get; set; } = "2026-11-18T10:00:00";
    public List<AgentScenario> Scenarios { get; set; } = [];
}

public sealed class AgentScenario
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    /// <summary>Signed-in user id (e.g. <c>eval-user</c>); <c>null</c> = anonymous visitor.</summary>
    public string? User { get; set; }

    /// <summary>Local (Europe/Kyiv) time of the conversation; defaults to the file's <c>now</c>.</summary>
    public string? Now { get; set; }

    public List<string> Turns { get; set; } = [];
    public ScenarioExpectation Expected { get; set; } = new();
}

/// <summary>Expectations for the last turn of a scenario.</summary>
public sealed class ScenarioExpectation
{
    /// <summary>First relevant tool the agent should call; <c>null</c> = no tool.</summary>
    public string? Tool { get; set; }

    /// <summary>Preparatory tools that may be called before the expected one without counting as wrong.</summary>
    public List<string> OptionalTools { get; set; } = [];

    /// <summary>Expected arguments of the expected tool (compared partially).</summary>
    public Dictionary<string, JsonElement> Args { get; set; } = [];

    /// <summary>Slots the agent must ask about (incomplete requests).</summary>
    public List<string> AsksFor { get; set; } = [];

    /// <summary>Tools that must not be executed in any turn; "*" = any tool.</summary>
    public List<string> MustNotCallTools { get; set; } = [];

    /// <summary>
    /// Tools that may be tried but must not succeed (e.g. a draft for a booked car: the attempt is rejected by the
    /// server and harmless, a created draft is a failure).
    /// </summary>
    public List<string> MustNotSucceedTools { get; set; } = [];

    public List<string> MustContain { get; set; } = [];
    public List<string> MustContainAny { get; set; } = [];
    public List<string> MustNotContain { get; set; } = [];
}

public sealed class QrelsFile
{
    public List<RetrievalQuery> Queries { get; set; } = [];
}

public sealed class RetrievalQuery
{
    public string Id { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public List<string> Relevant { get; set; } = [];
    public string Split { get; set; } = "test";
    public string Kind { get; set; } = "keyword";
    public string Lang { get; set; } = "uk";

    /// <summary>Fold 1..5 for k-fold cross-validation (stratified by kind and language).</summary>
    public int Fold { get; set; }

    /// <summary>original — the 56 queries of the first E3 run; expanded-kb — added with the expanded knowledge base.</summary>
    public string Origin { get; set; } = "original";
}

public static class Datasets
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ScenarioFile LoadScenarios(string path) =>
        JsonSerializer.Deserialize<ScenarioFile>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Cannot read {path}");

    public static QrelsFile LoadQrels(string path) =>
        JsonSerializer.Deserialize<QrelsFile>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Cannot read {path}");
}
