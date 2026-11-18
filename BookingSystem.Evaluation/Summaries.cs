using System.Globalization;

namespace BookingSystem.Evaluation;

/// <summary>
/// Aggregates agent runs: metrics are computed per repeat, then mean and sample standard deviation
/// across repeats are reported (summary_agent.csv), plus task success per scenario type.
/// </summary>
public static class AgentSummary
{
    public static readonly string[] Metrics =
    [
        "tool_selection_acc", "arg_acc", "task_success", "clarification_acc",
        "invalid_call_rate_proposed", "invalid_call_rate_executed",
        "latency_ms_avg", "latency_ms_p95", "tokens_per_turn", "cost_per_turn_usd", "degraded_rate"
    ];

    public static void Write(string runsCsv, string resultsDir)
    {
        var rows = Csv.Read(runsCsv);
        if (rows.Count == 0) return;

        var groups = rows.GroupBy(r => (Experiment: r["experiment"], Model: r["model"], SlotFilling: r["slot_filling"]));

        var summary = new List<object?[]>();
        var byType = new List<object?[]>();

        foreach (var group in groups.OrderBy(g => g.Key.Experiment).ThenBy(g => g.Key.Model).ThenBy(g => g.Key.SlotFilling))
        {
            var perRepeat = group.GroupBy(r => r["repeat"]).Select(Compute).ToList();
            var line = new List<object?>
            {
                group.Key.Experiment, group.Key.Model, group.Key.SlotFilling == "1" ? "on" : "off",
                perRepeat.Count, group.Select(r => r["scenario_id"]).Distinct().Count()
            };
            foreach (var metric in Metrics)
            {
                var values = perRepeat.Select(m => m[metric]).Where(v => !double.IsNaN(v)).ToList();
                line.Add(values.Count == 0 ? null : values.Average());
                line.Add(values.Count == 0 ? null : StdDev(values));
            }
            summary.Add([.. line]);

            foreach (var type in group.GroupBy(r => r["type"]).OrderBy(t => t.Key))
            {
                var perRepeatSuccess = type.GroupBy(r => r["repeat"]).Select(rep => rep.Average(r => r.B("task_success") ? 1.0 : 0.0)).ToList();
                byType.Add([
                    group.Key.Experiment, group.Key.Model, group.Key.SlotFilling == "1" ? "on" : "off", type.Key,
                    type.Select(r => r["scenario_id"]).Distinct().Count(), perRepeatSuccess.Average(), StdDev(perRepeatSuccess)
                ]);
            }
        }

        var header = new List<string> { "experiment", "model", "slot_filling", "repeats", "scenarios" };
        foreach (var metric in Metrics) { header.Add(metric + "_mean"); header.Add(metric + "_std"); }

        Csv.Write(Path.Combine(resultsDir, "summary_agent.csv"), header, summary);
        Csv.Write(Path.Combine(resultsDir, "summary_agent_by_type.csv"),
            ["experiment", "model", "slot_filling", "type", "scenarios", "task_success_mean", "task_success_std"], byType);
    }

    private static Dictionary<string, double> Compute(IEnumerable<Dictionary<string, string>> repeatRows)
    {
        var rows = repeatRows.ToList();
        var turns = rows.Sum(r => r.D("turns"));
        var latencies = rows.SelectMany(r => r["latency_ms_turns"].Split('|', StringSplitOptions.RemoveEmptyEntries))
            .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).OrderBy(v => v).ToList();
        var clarification = rows.Where(r => r.B("clarification_expected")).ToList();

        return new Dictionary<string, double>
        {
            ["tool_selection_acc"] = rows.Average(r => r.B("tool_selection_correct") ? 1.0 : 0.0),
            ["arg_acc"] = Ratio(rows.Sum(r => r.D("args_correct")), rows.Sum(r => r.D("args_expected"))),
            ["task_success"] = rows.Average(r => r.B("task_success") ? 1.0 : 0.0),
            ["clarification_acc"] = clarification.Count == 0 ? double.NaN : clarification.Average(r => r.B("clarification_correct") ? 1.0 : 0.0),
            ["invalid_call_rate_proposed"] = Ratio(rows.Sum(r => r.D("proposed_invalid_calls")), rows.Sum(r => r.D("slot_tool_calls"))),
            ["invalid_call_rate_executed"] = Ratio(rows.Sum(r => r.D("executed_invalid_calls")), rows.Sum(r => r.D("executed_calls"))),
            ["latency_ms_avg"] = latencies.Count == 0 ? double.NaN : latencies.Average(),
            ["latency_ms_p95"] = Percentile(latencies, 0.95),
            ["tokens_per_turn"] = Ratio(rows.Sum(r => r.D("input_tokens") + r.D("output_tokens")), turns),
            ["cost_per_turn_usd"] = Ratio(rows.Sum(r => r.D("cost_usd")), turns),
            ["degraded_rate"] = rows.Average(r => r.B("degraded") ? 1.0 : 0.0)
        };
    }

    private static double Ratio(double a, double b) => b == 0 ? double.NaN : a / b;

    internal static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    internal static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return double.NaN;
        var rank = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }
}
