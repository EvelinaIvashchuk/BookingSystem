using BookingSystem.Evaluation;
using FluentAssertions;

namespace BookingSystem.Tests.Evaluation;

public sealed class RescoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rescore-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Rescore_AcceptsPoliteRequest_AndRecomputesTaskSuccess()
    {
        var scenarios = Path.Combine(_dir, "scenarios.json");
        File.WriteAllText(scenarios, """
            {"now":"2026-11-18T10:00:00","scenarios":[
              {"id":"I-01","type":"incomplete","turns":["Хочу авто"],
               "expected":{"tool":null,"asksFor":["PickupDate","ReturnDate"],"mustNotCallTools":["search_available_cars"]}},
              {"id":"S-01","type":"simple","turns":["x"],"expected":{"tool":"search_available_cars"}}]}
            """);
        var runs = Path.Combine(_dir, "runs.csv");
        Csv.Write(runs, AgentExperiment.Header,
        [
            Row("I-01", "Будь ласка, вкажіть дати оренди (день отримання та день повернення).", toolOk: true, clarification: false, success: false),
            Row("I-01", "Вкажіть, будь ласка, місто.", toolOk: true, clarification: false, success: false),
            Row("S-01", "Ось авто.", toolOk: false, clarification: false, success: false)
        ]);

        var result = AgentRescore.Run(runs, scenarios);

        result.Should().Be(new AgentRescore.Result(Rows: 3, ClarificationChanged: 1, SuccessChanged: 1));
        var rows = Csv.Read(runs);
        rows.Select(r => (r["clarification_correct"], r["task_success"]))
            .Should().Equal(("1", "1"), ("0", "0"), ("0", "0"));
        rows[0]["final_reply"].Should().StartWith("Будь ласка");
    }

    private static object?[] Row(string id, string reply, bool toolOk, bool clarification, bool success)
    {
        var values = AgentExperiment.Header.ToDictionary(h => h, _ => (object?)"");
        values["experiment"] = "E1";
        values["model"] = "gemini:test";
        values["slot_filling"] = true;
        values["scenario_id"] = id;
        values["repeat"] = 1;
        values["tool_selection_correct"] = toolOk;
        values["args_expected"] = 0;
        values["args_correct"] = 0;
        values["clarification_expected"] = id.StartsWith('I');
        values["clarification_correct"] = clarification;
        values["forbidden_call"] = false;
        values["content_ok"] = true;
        values["task_success"] = success;
        values["degraded"] = false;
        values["final_reply"] = reply;
        return [.. AgentExperiment.Header.Select(h => values[h])];
    }
}
