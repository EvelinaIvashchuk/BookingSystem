using BookingSystem.Evaluation;
using FluentAssertions;

namespace BookingSystem.Tests.Evaluation;

public class GroundingReplayTests
{
    private static readonly string ScenariosJson =
        Path.Combine(RepoRoot.Find(), "BookingSystem.Evaluation", "datasets", "agent_scenarios.json");

    private static object?[] Row(string id, string type, bool success, string callsJson) =>
        AgentExperiment.Header.Select(h => (object?)(h switch
        {
            "experiment" => "E1", "model" => "m", "slot_filling" => "1", "scenario_id" => id, "type" => type,
            "repeat" => 1, "task_success" => success, "tool_calls_json" => callsJson, _ => ""
        })).ToArray();

    private static string Search(string pickup, string ret) =>
        $$"""[[{"Name":"search_available_cars","ArgumentsJson":"{\"pickupDate\":\"{{pickup}}\",\"returnDate\":\"{{ret}}\"}","Success":true,"Intercepted":false,"EffectiveArgumentsJson":null}]]""";

    [Fact]
    public void Replay_ClassifiesInventedWrongAndCorrectDates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grounding-replay-" + Guid.NewGuid().ToString("N"));
        var csv = Path.Combine(dir, "agent_runs.csv");
        Csv.Write(csv, AgentExperiment.Header,
        [
            Row("I-07", "incomplete", false, Search("2026-12-10", "2026-12-11")),   // "from December 10": return invented
            Row("R-03", "relative-dates", false, Search("2026-11-21", "2026-11-23")), // Saturday instead of Monday
            Row("S-04", "simple", true, Search("2026-12-03", "2026-12-06"))          // "з 3 по 6 грудня": grounded
        ]);

        var result = GroundingReplay.Run(csv, ScenariosJson, dir);

        result.DateCalls.Should().Be(3);
        result.Prevented.Should().Be(1);
        result.WrongDates.Should().Be(1);
        result.FalseBlocks.Should().Be(0);

        var summary = Csv.Read(Path.Combine(dir, "summary_grounding_replay.csv")).Single();
        summary["task_success"].Should().Be("0.333");
        summary["task_success_projected"].Should().Be("0.667", "the prevented invented date becomes a clarification");

        Directory.Delete(dir, recursive: true);
    }
}
