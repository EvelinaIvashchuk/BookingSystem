using System.Globalization;
using System.Text.Json;
using BookingSystem.Ai;
using BookingSystem.Ai.Tools;
using BookingSystem.Data;
using BookingSystem.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookingSystem.Evaluation;

/// <summary>
/// Offline evaluation of the amount check (<see cref="AmountGrounding"/>, <c>Ai:AmountCheck</c>) on replies already
/// recorded in agent_runs.csv (E1, E2, E1-v2) and security_runs.csv (E4) — no model calls.
/// <para>
/// The tool calls of every run are executed again, with the arguments the tools actually received, against the same
/// deterministic evaluation database and the scenario's pinned "today"; their results plus the user's turns are the
/// evidence, exactly as in <see cref="AgentService"/>. The recorded final reply is then checked:
/// </para>
/// <list type="bullet">
/// <item>a changed reply in a scenario where the agent should not quote that amount (E4 X-15) is a <b>caught attack</b>;</item>
/// <item>a changed reply in any other scenario is a <b>false positive</b> — the check removed a sentence from a
///       legitimate reply; the scenario is re-scored on the redacted text to see whether the task would now fail.</item>
/// </list>
/// Limitations: final replies are stored truncated to 400 characters; intermediate assistant replies are not stored
/// (their amounts came from earlier tool results, which are replayed); policy search embeds the query again
/// (a few embedding calls, results cached per query).
/// </summary>
public sealed class AmountReplay(IServiceScopeFactory scopes, ILogger<AmountReplay> logger)
{
    public static readonly string[] Header =
    [
        "source", "experiment", "model", "scenario_id", "type", "repeat", "amounts", "ungrounded", "task_success",
        "task_success_after", "final_reply", "reply_after"
    ];

    public static readonly string[] SummaryHeader =
    [
        "source", "experiment", "model", "runs", "replies_with_amounts", "changed_runs", "task_success",
        "task_success_after", "success_lost", "success_gained"
    ];

    public sealed record Input(string Source, string RunsCsv, string ScenariosJson);

    public sealed record Result(int Runs, int WithAmounts, int Changed, int SuccessLost, int SuccessGained);

    private readonly Dictionary<string, string> _cache = [];

    public async Task<Result> RunAsync(IReadOnlyList<Input> inputs, string outputDirectory, CancellationToken ct)
    {
        var changedRows = new List<object?[]>();
        var perRun = new List<(string Source, Dictionary<string, string> Row, bool HasAmounts, bool Changed, bool Before, bool After)>();

        foreach (var input in inputs)
        {
            if (!File.Exists(input.RunsCsv)) continue;
            var file = Datasets.LoadScenarios(input.ScenariosJson);
            var scenarios = file.Scenarios.ToDictionary(s => s.Id);

            foreach (var row in Csv.Read(input.RunsCsv))
            {
                if (row.B("degraded") || !scenarios.TryGetValue(row["scenario_id"], out var scenario)) continue;

                var reply = row["final_reply"];
                var amounts = AmountGrounding.FindAmounts(reply);
                var before = row.B("task_success");
                if (amounts.Count == 0)
                {
                    perRun.Add((input.Source, row, false, false, before, before));
                    continue;
                }

                var evidence = await ReplayToolsAsync(row, scenario, file.Now, ct);
                evidence.AddRange(scenario.Turns);
                var check = AmountGrounding.Check(reply, evidence);

                var after = before;
                if (check.Changed)
                {
                    var e = scenario.Expected;
                    var text = ScenarioScorer.NormalizeText(check.Text);
                    var contentOk = e.MustContain.All(s => text.Contains(ScenarioScorer.NormalizeText(s)))
                                    && (e.MustContainAny.Count == 0 || e.MustContainAny.Any(s => text.Contains(ScenarioScorer.NormalizeText(s))))
                                    && !e.MustNotContain.Any(s => text.Contains(ScenarioScorer.NormalizeText(s)));
                    var clarificationOk = e.AsksFor.Count == 0
                                          || (!row.B("forbidden_call") && ScenarioScorer.IsClarification(check.Text, e.AsksFor));
                    after = row.B("tool_selection_correct")
                            && row["args_correct"] == row["args_expected"]
                            && clarificationOk
                            && !row.B("forbidden_call")
                            && contentOk;

                    changedRows.Add(
                    [
                        input.Source, row["experiment"], row["model"], row["scenario_id"], row["type"], row["repeat"],
                        string.Join(" ", amounts.Select(a => a.ToString(CultureInfo.InvariantCulture))),
                        string.Join(" ", check.Ungrounded.Select(a => a.ToString(CultureInfo.InvariantCulture))),
                        before ? 1 : 0, after ? 1 : 0, reply, check.Text
                    ]);
                }

                perRun.Add((input.Source, row, true, check.Changed, before, after));
            }
        }

        Csv.Write(Path.Combine(outputDirectory, "amount_replay.csv"), Header, changedRows);

        var summary = perRun
            .GroupBy(r => (r.Source, Experiment: r.Row["experiment"], Model: r.Row["model"]))
            .OrderBy(g => g.Key.Source).ThenBy(g => g.Key.Experiment).ThenBy(g => g.Key.Model)
            .Select(g => (object?[])
            [
                g.Key.Source, g.Key.Experiment, g.Key.Model, g.Count(), g.Count(r => r.HasAmounts), g.Count(r => r.Changed),
                g.Count(r => r.Before), g.Count(r => r.After),
                g.Count(r => r.Before && !r.After), g.Count(r => !r.Before && r.After)
            ]);
        Csv.Write(Path.Combine(outputDirectory, "summary_amount_replay.csv"), SummaryHeader, summary);

        return new Result(perRun.Count, perRun.Count(r => r.HasAmounts), perRun.Count(r => r.Changed),
            perRun.Count(r => r.Before && !r.After), perRun.Count(r => !r.Before && r.After));
    }

    /// <summary>Runs the recorded (non-intercepted) tool calls of every turn again and returns their results as JSON.</summary>
    private async Task<List<string>> ReplayToolsAsync(
        Dictionary<string, string> row, AgentScenario scenario, string defaultNow, CancellationToken ct)
    {
        var outputs = new List<string>();
        var turns = JsonSerializer.Deserialize<List<List<JsonElement>>>(row["tool_calls_json"]) ?? [];
        if (turns.All(t => t.Count == 0)) return outputs;

        var now = scenario.Now ?? defaultNow;
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ScenarioTimeProvider>().SetLocalNow(DateTime.Parse(now, CultureInfo.InvariantCulture));
        var clock = sp.GetRequiredService<AgentClock>();
        var factory = sp.GetRequiredService<BookingToolsFactory>();
        Guid? sessionId = null;

        for (var t = 0; t < turns.Count; t++)
        {
            foreach (var call in turns[t])
            {
                if (call.GetProperty("Intercepted").GetBoolean()) continue;

                var name = call.GetProperty("Name").GetString()!;
                var args = call.TryGetProperty("EffectiveArgumentsJson", out var eff) && eff.ValueKind == JsonValueKind.String
                    ? eff.GetString()!
                    : call.GetProperty("ArgumentsJson").GetString()!;
                var key = $"{name}|{scenario.User}|{now}|{args}";
                if (_cache.TryGetValue(key, out var cached))
                {
                    outputs.Add(cached);
                    continue;
                }

                sessionId ??= await CreateSessionAsync(sp, scenario.User, ct);
                var turn = new AgentTurnContext(sessionId.Value, scenario.User, clock.Today,
                    scenario.Turns.ElementAtOrDefault(t), slotFillingEnabled: false);
                var function = factory.Create(turn).AsAITools().OfType<AIFunction>().FirstOrDefault(f => f.Name == name);
                if (function is null) continue;

                string output;
                try
                {
                    var arguments = new AIFunctionArguments(ScenarioScorer.ParseArgs(args).ToDictionary(a => a.Key, a => (object?)a.Value));
                    var result = await function.InvokeAsync(arguments, ct);
                    output = result is null ? "" : JsonSerializer.Serialize(result, result.GetType(), AIJsonUtilities.DefaultOptions);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogDebug(ex, "Replay of {Tool}({Args}) failed", name, args);
                    output = "";
                }

                _cache[key] = output;
                outputs.Add(output);
            }
        }
        return outputs;
    }

    private static async Task<Guid> CreateSessionAsync(IServiceProvider sp, string? userId, CancellationToken ct)
    {
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var now = DateTime.UtcNow;
        var session = new ChatSession { Id = Guid.NewGuid(), UserId = userId, CreatedAt = now, UpdatedAt = now };
        await uow.ChatSessions.AddAsync(session, ct);
        await uow.CommitAsync(ct);
        return session.Id;
    }
}
