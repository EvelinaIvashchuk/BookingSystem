using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BookingSystem.Ai;
using BookingSystem.Ai.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingSystem.Evaluation;

public sealed record AgentRunPlan(
    string Experiment,
    IReadOnlyList<string> Models,
    int Repeats,
    IReadOnlyList<bool> SlotFillingModes,
    string OutputCsv,
    IReadOnlySet<string>? Types = null,
    IReadOnlySet<string>? Ids = null,
    int? DelayMs = null);

/// <summary>
/// Runs agent scenarios (E1 models, E2 slot-filling ablation). One CSV row per
/// (experiment, model, slot filling, scenario, repeat). Resumable: rows already present are skipped.
/// Models run in parallel (separate provider rate limits); scenarios of one model run sequentially.
/// Transient provider errors (429/5xx/timeouts) are retried with exponential back-off;
/// a daily-quota error stops that model so the run can be resumed later.
/// </summary>
public sealed class AgentExperiment(
    IServiceScopeFactory scopes,
    IOptions<EvaluationOptions> evalOptions,
    IOptions<AiOptions> aiOptions,
    EvalPaths paths,
    ILogger<AgentExperiment> logger)
{
    public static readonly string[] Header =
    [
        "experiment", "model", "slot_filling", "scenario_id", "type", "repeat", "turns",
        "tool_expected", "tool_first", "tool_selection_correct", "args_expected", "args_correct", "arg_mismatches",
        "clarification_expected", "clarification_correct", "forbidden_call", "content_ok", "task_success",
        "tool_calls", "slot_tool_calls", "intercepted_calls", "proposed_invalid_calls", "executed_calls", "executed_invalid_calls",
        "missing_slots", "latency_ms_total", "latency_ms_turns", "input_tokens", "output_tokens", "cost_usd",
        "degraded", "attempts", "final_reply", "tool_calls_json", "timestamp_utc"
    ];

    public async Task RunAsync(AgentRunPlan plan, CancellationToken ct)
    {
        var file = Datasets.LoadScenarios(Path.Combine(paths.Datasets, "agent_scenarios.json"));
        var scenarios = file.Scenarios
            .Where(s => plan.Types is null || plan.Types.Contains(s.Type))
            .Where(s => plan.Ids is null || plan.Ids.Contains(s.Id))
            .ToList();

        var done = Csv.Read(plan.OutputCsv).Select(Key).ToHashSet();
        logger.LogInformation(
            "{Experiment}: {Scenarios} scenarios × {Repeats} repeats × {Modes} mode(s) × models [{Models}]; {Done} rows already in {Csv}",
            plan.Experiment, scenarios.Count, plan.Repeats, plan.SlotFillingModes.Count, string.Join(", ", plan.Models),
            done.Count, Path.GetFileName(plan.OutputCsv));

        await Task.WhenAll(plan.Models.Select(model => RunModelAsync(plan, model, scenarios, file.Now, done, ct)));
    }

    private async Task RunModelAsync(
        AgentRunPlan plan, string model, List<AgentScenario> scenarios, string defaultNow, HashSet<string> done, CancellationToken ct)
    {
        var delay = plan.DelayMs ?? DefaultDelay(model);
        var completed = 0;

        foreach (var slotFilling in plan.SlotFillingModes)
        for (var repeat = 1; repeat <= plan.Repeats; repeat++)
        foreach (var scenario in scenarios)
        {
            ct.ThrowIfCancellationRequested();
            var key = Key(plan.Experiment, model, slotFilling, scenario.Id, repeat);
            if (done.Contains(key)) continue;

            var (replies, attempts, dailyLimit) = await RunWithRetriesAsync(model, slotFilling, scenario, defaultNow, delay, ct);
            if (dailyLimit)
            {
                logger.LogWarning("{Model}: daily quota reached — stopping this model. Re-run the same command later to resume.", model);
                return;
            }

            var score = ScenarioScorer.Score(scenario, replies);
            await Csv.AppendAsync(plan.OutputCsv, Header, Row(plan.Experiment, model, slotFilling, scenario, repeat, replies, score, attempts));
            completed++;

            logger.LogInformation("{Model} sf={SlotFilling} r{Repeat} {Scenario}: {Result} ({Tool}) [{Completed}]",
                model, slotFilling ? "on" : "off", repeat, scenario.Id, score.TaskSuccess ? "OK  " : "FAIL",
                score.FirstRelevantTool ?? "no tool", completed);
        }

        logger.LogInformation("{Model}: finished, {Completed} new rows", model, completed);
    }

    private async Task<(List<AgentReply> Replies, int Attempts, bool DailyLimit)> RunWithRetriesAsync(
        string model, bool slotFilling, AgentScenario scenario, string defaultNow, int delayMs, CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, evalOptions.Value.MaxScenarioAttempts);
        List<AgentReply> replies = [];

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            replies = await RunOnceAsync(model, slotFilling, scenario, defaultNow, delayMs, ct);
            var failed = replies.LastOrDefault(r => r.IsDegraded);
            if (failed is null) return (replies, attempt, false);

            if (IsDailyLimit(failed.ErrorDetail)) return (replies, attempt, true);

            var wait = TimeSpan.FromSeconds(Math.Min(300, 20 * Math.Pow(2, attempt - 1)));
            logger.LogWarning("{Model} {Scenario}: provider error (attempt {Attempt}/{Max}), retrying in {Wait:0}s: {Error}",
                model, scenario.Id, attempt, maxAttempts, wait.TotalSeconds, Short(failed.ErrorDetail));
            await Task.Delay(wait, ct);
        }

        return (replies, maxAttempts, false);   // recorded as degraded (task failed)
    }

    private async Task<List<AgentReply>> RunOnceAsync(
        string model, bool slotFilling, AgentScenario scenario, string defaultNow, int delayMs, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScenarioTimeProvider>()
            .SetLocalNow(DateTime.Parse(scenario.Now ?? defaultNow, CultureInfo.InvariantCulture));
        var agent = scope.ServiceProvider.GetRequiredService<IAgentService>();

        var replies = new List<AgentReply>();
        var sessionId = Guid.Empty;
        var options = new AgentChatOptions { Model = model, SlotFillingEnabled = slotFilling };

        foreach (var turn in scenario.Turns)
        {
            var reply = await agent.ChatAsync(sessionId, scenario.User, turn, options, ct);
            replies.Add(reply);
            sessionId = reply.SessionId;
            if (reply.IsDegraded) break;
            await Task.Delay(delayMs, ct);
        }
        return replies;
    }

    private IEnumerable<object?> Row(
        string experiment, string model, bool slotFilling, AgentScenario scenario, int repeat,
        List<AgentReply> replies, ScenarioScore score, int attempts)
    {
        var input = replies.Sum(r => r.InputTokens ?? 0);
        var output = replies.Sum(r => r.OutputTokens ?? 0);
        var pricing = aiOptions.Value.Pricing.FirstOrDefault(p => string.Equals(p.Model, model, StringComparison.OrdinalIgnoreCase));
        var cost = pricing is null ? 0m : (input * pricing.InputPer1M + output * pricing.OutputPer1M) / 1_000_000m;
        var last = replies[^1];

        return
        [
            experiment, model, slotFilling, scenario.Id, scenario.Type, repeat, replies.Count,
            scenario.Expected.Tool ?? "", score.FirstRelevantTool ?? "", score.ToolSelectionCorrect,
            score.ArgsExpected, score.ArgsCorrect, score.ArgMismatches,
            score.ClarificationExpected, score.ClarificationCorrect, score.ForbiddenCall, score.ContentOk, score.TaskSuccess,
            replies.Sum(r => r.ToolCalls.Count), score.SlotToolCalls, score.InterceptedCalls, score.ProposedInvalidCalls,
            score.ExecutedCalls, score.ExecutedInvalidCalls,
            string.Join('|', last.MissingSlots), replies.Sum(r => r.LatencyMs), string.Join('|', replies.Select(r => r.LatencyMs)),
            input, output, cost,
            replies.Any(r => r.IsDegraded), attempts,
            Truncate(last.Text, 400),
            JsonSerializer.Serialize(replies.Select(r => r.ToolCalls)),
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        ];
    }

    private int DefaultDelay(string model) =>
        model.StartsWith("gemini:", StringComparison.OrdinalIgnoreCase) ? 4500 : evalOptions.Value.DelayBetweenTurnsMs;

    /// <summary>Provider messages for daily quotas (Groq: "tokens per day (TPD)", Gemini: "PerDay", "RPD").</summary>
    internal static bool IsDailyLimit(string? error) =>
        error is not null
        && (error.Contains("per day", StringComparison.OrdinalIgnoreCase)
            || error.Contains("PerDay", StringComparison.Ordinal)
            || error.Contains("(TPD)", StringComparison.Ordinal)
            || error.Contains("(RPD)", StringComparison.Ordinal)
            || error.Contains("requests_per_day", StringComparison.OrdinalIgnoreCase));

    private static string Key(Dictionary<string, string> row) =>
        Key(row["experiment"], row["model"], row["slot_filling"] == "1", row["scenario_id"], int.Parse(row["repeat"], CultureInfo.InvariantCulture));

    private static string Key(string experiment, string model, bool slotFilling, string scenario, int repeat) =>
        $"{experiment}|{model}|{(slotFilling ? 1 : 0)}|{scenario}|{repeat}";

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private static string Short(string? text) => text is null ? "" : Truncate(text.ReplaceLineEndings(" "), 220);
}
