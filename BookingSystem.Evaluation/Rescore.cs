namespace BookingSystem.Evaluation;

/// <summary>
/// Re-applies the clarification criterion of <see cref="ScenarioScorer"/> to rows already in agent_runs.csv,
/// so that every row is scored by the same rule after the criterion changed. Uses only stored columns:
/// the final reply (short for clarifications, so the 400-character truncation does not matter) and the other
/// components of task success (tool selection, arguments, forbidden calls, content, degraded).
/// </summary>
public static class AgentRescore
{
    public sealed record Result(int Rows, int ClarificationChanged, int SuccessChanged);

    public static Result Run(string runsCsv, string scenariosJson)
    {
        var scenarios = Datasets.LoadScenarios(scenariosJson).Scenarios.ToDictionary(s => s.Id);
        var rows = Csv.Read(runsCsv);
        int clarificationChanged = 0, successChanged = 0;

        foreach (var row in rows)
        {
            if (!scenarios.TryGetValue(row["scenario_id"], out var scenario)) continue;
            var asksFor = scenario.Expected.AsksFor;
            var clarificationExpected = asksFor.Count > 0;

            var clarification = clarificationExpected
                                && !row.B("forbidden_call")
                                && ScenarioScorer.IsClarification(row["final_reply"], asksFor);
            var success = !row.B("degraded")
                          && row.B("tool_selection_correct")
                          && row["args_correct"] == row["args_expected"]
                          && (!clarificationExpected || clarification)
                          && !row.B("forbidden_call")
                          && row.B("content_ok");

            if (Flag(clarification) != row["clarification_correct"]) clarificationChanged++;
            if (Flag(success) != row["task_success"]) successChanged++;
            row["clarification_correct"] = Flag(clarification);
            row["task_success"] = Flag(success);
        }

        if (clarificationChanged + successChanged > 0)
            Csv.Write(runsCsv, AgentExperiment.Header, rows.Select(r => AgentExperiment.Header.Select(h => (object?)r[h])));

        return new Result(rows.Count, clarificationChanged, successChanged);
    }

    private static string Flag(bool value) => value ? "1" : "0";
}
