using System.Globalization;
using System.Text.Json;
using BookingSystem.Ai.Slots;

namespace BookingSystem.Evaluation;

/// <summary>
/// Offline estimate of the date grounding check (<see cref="DateGrounding"/>, <c>Ai:SlotFilling:RequireGroundedDates</c>)
/// on calls already recorded in agent_runs.csv — no model calls, no cost.
/// <para>
/// For every executed tool call with dates, the dates the model itself proposed are checked against the user turns of
/// the scenario up to that call and against dates stored by earlier successful calls (slot state). A flagged call is:
/// </para>
/// <list type="bullet">
/// <item><b>prevented</b> — in a scenario where the agent had to ask (incomplete): the check would have blocked an
///       invented date;</item>
/// <item><b>wrong date</b> — in a scenario that expects this tool, a date that differs from the expected one
///       (the check caught a model error that the weekday/duration checks missed);</item>
/// <item><b>false block</b> — the expected, correct date would have been blocked.</item>
/// </list>
/// Limitation: intermediate assistant replies are not stored, so dates proposed by the assistant and accepted by the
/// user are not seen (this can only overstate false blocks); the effect on task success assumes that a blocked call
/// leads to a clarifying question, as the models did on every other <c>invalid_slots</c> result.
/// </summary>
public static class GroundingReplay
{
    public static readonly string[] Header =
    [
        "experiment", "model", "slot_filling", "scenario_id", "type", "repeat", "turn", "tool", "argument", "date",
        "verdict", "task_success", "user_turns"
    ];

    public static readonly string[] SummaryHeader =
    [
        "experiment", "model", "slot_filling", "runs", "date_calls", "flagged_calls", "flagged_runs",
        "prevented_runs", "wrong_date_runs", "false_block_runs", "task_success", "task_success_projected"
    ];

    private static readonly HashSet<string> DateTools =
    [
        "search_available_cars", "calculate_price", "create_rental_draft"
    ];

    public sealed record Result(int Runs, int DateCalls, int Flagged, int Prevented, int WrongDates, int FalseBlocks);

    public static Result Run(string runsCsv, string scenariosJson, string outputDirectory)
    {
        var file = Datasets.LoadScenarios(scenariosJson);
        var scenarios = file.Scenarios.ToDictionary(s => s.Id);
        var rows = Csv.Read(runsCsv);

        var flaggedRows = new List<object?[]>();
        var perRun = new List<(Dictionary<string, string> Row, int DateCalls, int Flagged, string? Verdict)>();

        foreach (var row in rows)
        {
            if (!scenarios.TryGetValue(row["scenario_id"], out var scenario)) continue;
            var today = DateOnly.FromDateTime(DateTime.Parse(scenario.Now ?? file.Now, CultureInfo.InvariantCulture));
            var expectsDates = scenario.Expected.Tool is not null && scenario.Expected.Args.ContainsKey("pickupDate");
            var asks = scenario.Expected.AsksFor.Count > 0;

            var turns = JsonSerializer.Deserialize<List<List<JsonElement>>>(row["tool_calls_json"]) ?? [];
            var known = new Dictionary<string, DateOnly>();
            int dateCalls = 0, flagged = 0;
            string? runVerdict = null;

            for (var t = 0; t < turns.Count && t < scenario.Turns.Count; t++)
            {
                var userTurns = scenario.Turns.Take(t + 1).ToList();
                foreach (var call in turns[t])
                {
                    var name = call.GetProperty("Name").GetString()!;
                    if (!DateTools.Contains(name) || call.GetProperty("Intercepted").GetBoolean()) continue;

                    var proposed = Dates(call.GetProperty("ArgumentsJson").GetString());
                    if (proposed.Count == 0) continue;
                    dateCalls++;

                    foreach (var (argument, date) in proposed)
                    {
                        var isReturn = argument == SlotSchema.ReturnDateArg;
                        var context = new DateGroundingContext(today, userTurns, null,
                            known.TryGetValue(argument, out var k) ? k : null);
                        if (DateGrounding.IsGrounded(date, isReturn, context)) continue;

                        flagged++;
                        var verdict = Verdict(scenario, asks, expectsDates, argument, date);
                        runVerdict = Worse(runVerdict, verdict);
                        flaggedRows.Add(
                        [
                            row["experiment"], row["model"], row["slot_filling"], row["scenario_id"], row["type"], row["repeat"],
                            t + 1, name, argument, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            verdict, row["task_success"], string.Join(" | ", userTurns)
                        ]);
                    }

                    // Slot state after a successful call (as SlotFillingEngine.Commit does with slot filling on).
                    if (row["slot_filling"] == "1" && call.GetProperty("Success").GetBoolean())
                    {
                        var effective = call.TryGetProperty("EffectiveArgumentsJson", out var e) && e.ValueKind == JsonValueKind.String
                            ? e.GetString()
                            : call.GetProperty("ArgumentsJson").GetString();
                        foreach (var (argument, date) in Dates(effective))
                            known[argument] = date;
                    }
                }
            }

            perRun.Add((row, dateCalls, flagged, runVerdict));
        }

        Directory.CreateDirectory(outputDirectory);
        Csv.Write(Path.Combine(outputDirectory, "grounding_replay.csv"), Header, flaggedRows);

        var summary = perRun
            .GroupBy(r => (r.Row["experiment"], r.Row["model"], r.Row["slot_filling"]))
            .OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2)
            .Select(g =>
            {
                var runs = g.ToList();
                double success = runs.Average(r => r.Row.B("task_success") ? 1.0 : 0.0);
                // Projection: a prevented invented date turns a failed clarification into a success;
                // a false block turns a successful run into a failure (worst case); a caught wrong date keeps the
                // original outcome (the model may recompute the date or ask, and asking still fails the scenario).
                double projected = runs.Average(r => r.Verdict switch
                {
                    "prevented" => 1.0,
                    "false_block" => 0.0,
                    _ => r.Row.B("task_success") ? 1.0 : 0.0
                });
                return new object?[]
                {
                    g.Key.Item1, g.Key.Item2, g.Key.Item3, runs.Count, runs.Sum(r => r.DateCalls), runs.Sum(r => r.Flagged),
                    runs.Count(r => r.Flagged > 0), runs.Count(r => r.Verdict == "prevented"),
                    runs.Count(r => r.Verdict == "wrong_date"), runs.Count(r => r.Verdict == "false_block"), Math.Round(success, 3), Math.Round(projected, 3)
                };
            })
            .ToList();
        Csv.Write(Path.Combine(outputDirectory, "summary_grounding_replay.csv"), SummaryHeader, summary);

        return new Result(
            perRun.Count, perRun.Sum(r => r.DateCalls), perRun.Sum(r => r.Flagged),
            perRun.Count(r => r.Verdict == "prevented"), perRun.Count(r => r.Verdict == "wrong_date"),
            perRun.Count(r => r.Verdict == "false_block"));
    }

    /// <summary>
    /// prevented — an invented date where the agent had to ask; wrong_date — a date that differs from the expected one
    /// (the check caught a model error); false_block — the expected, correct date was blocked; other — anything else.
    /// </summary>
    private static string Verdict(AgentScenario scenario, bool asks, bool expectsDates, string argument, DateOnly date)
    {
        if (asks) return "prevented";
        if (!expectsDates) return "other";
        return scenario.Expected.Args.TryGetValue(argument, out var expected)
               && SlotValues.TryParseDate(expected.GetString(), out var expectedDate)
               && expectedDate == date
            ? "false_block"
            : "wrong_date";
    }

    private static readonly string[] Severity = ["other", "wrong_date", "prevented", "false_block"];

    private static string Worse(string? current, string next) =>
        current is null || Array.IndexOf(Severity, next) > Array.IndexOf(Severity, current) ? next : current;

    private static List<(string Argument, DateOnly Date)> Dates(string? argumentsJson)
    {
        var list = new List<(string, DateOnly)>();
        if (string.IsNullOrWhiteSpace(argumentsJson)) return list;

        using var doc = JsonDocument.Parse(argumentsJson);
        foreach (var argument in new[] { SlotSchema.PickupDateArg, SlotSchema.ReturnDateArg })
            if (doc.RootElement.TryGetProperty(argument, out var value)
                && value.ValueKind == JsonValueKind.String
                && SlotValues.TryParseDate(value.GetString(), out var date))
                list.Add((argument, date));
        return list;
    }
}
