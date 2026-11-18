using BookingSystem.Evaluation;
using FluentAssertions;

namespace BookingSystem.Tests.Evaluation;

public class RunPlanTests
{
    [Theory]
    [InlineData("gemini:gemini-3.8-flash@3", "gemini:gemini-3.8-flash", 3)]
    [InlineData("groq:openai/gpt-oss-120b", "groq:openai/gpt-oss-120b", 1)]
    [InlineData(" gemini:gemini-3.1-flash-lite@2 ", "gemini:gemini-3.1-flash-lite", 2)]
    public void ModelRun_ParsesPerModelRepeats(string spec, string model, int repeats)
    {
        ModelRun.Parse(spec, defaultRepeats: 1).Should().Be(new ModelRun(model, repeats));
    }

    private static Dictionary<string, string> Row(string experiment, string model, bool slotFilling, int repeat) => new()
    {
        ["experiment"] = experiment, ["model"] = model, ["slot_filling"] = slotFilling ? "1" : "0",
        ["repeat"] = repeat.ToString(), ["scenario_id"] = "S-01"
    };

    [Fact]
    public void E2Summary_ReusesE1OnRowsOnlyForE2ModelsAndRunRepeats()
    {
        var rows = new List<Dictionary<string, string>>
        {
            Row("E1", "m-strong", true, 1), Row("E1", "m-strong", true, 2), Row("E1", "m-strong", true, 3),
            Row("E1", "m-other", true, 1),
            Row("E2", "m-strong", false, 1), Row("E2", "m-strong", false, 2)
        };

        var reused = AgentSummary.ReusedE1Rows(rows).ToList();

        reused.Should().HaveCount(2).And.OnlyContain(r => r["experiment"] == "E2" && r["model"] == "m-strong" && r["slot_filling"] == "1");
        reused.Select(r => r["repeat"]).Should().BeEquivalentTo(["1", "2"]);
    }

    [Fact]
    public void E2Summary_DoesNotReuseWhenOnConditionWasRunExplicitly()
    {
        var rows = new List<Dictionary<string, string>>
        {
            Row("E1", "m", true, 1), Row("E2", "m", true, 1), Row("E2", "m", false, 1)
        };

        AgentSummary.ReusedE1Rows(rows).Should().BeEmpty();
    }
}
