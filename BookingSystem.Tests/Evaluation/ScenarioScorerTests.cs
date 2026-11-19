using System.Text.Json;
using BookingSystem.Ai.Dtos;
using BookingSystem.Evaluation;
using FluentAssertions;

namespace BookingSystem.Tests.Evaluation;

public class ScenarioScorerTests
{
    private static AgentScenario Scenario(string? tool, object? args = null, string[]? asksFor = null,
        string[]? mustNotCall = null, string[]? mustContain = null, string[]? optional = null) => new()
    {
        Id = "T", Type = "test", Turns = ["x"],
        Expected = new ScenarioExpectation
        {
            Tool = tool,
            Args = args is null ? [] : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(args))!,
            AsksFor = [.. asksFor ?? []],
            MustNotCallTools = [.. mustNotCall ?? []],
            MustContain = [.. mustContain ?? []],
            OptionalTools = [.. optional ?? []]
        }
    };

    private static AgentReply Reply(string text, params ToolCallRecord[] calls) => new() { Text = text, ToolCalls = calls };

    private static ToolCallRecord Call(string name, object args, bool success = true, bool intercepted = false,
        string? effective = null, string[]? proposedInvalid = null, string? error = null) =>
        new(name, JsonSerializer.Serialize(args), success, 10, error)
        {
            Intercepted = intercepted, EffectiveArgumentsJson = effective, ProposedInvalid = proposedInvalid
        };

    [Fact]
    public void CorrectSearch_WithSynonymsAndIsoDates_IsFullSuccess()
    {
        var scenario = Scenario("search_available_cars",
            new { pickupDate = "2026-12-20", returnDate = "2026-12-25", transmission = "Automatic", minSeats = 5, location = "Київ" });
        var reply = Reply("Ось варіанти", Call("search_available_cars",
            new { pickupDate = "2026-12-20T00:00:00", returnDate = "2026-12-25", transmission = "автомат", minSeats = "5", location = "Києві" }));

        var score = ScenarioScorer.Score(scenario, [reply]);

        score.ToolSelectionCorrect.Should().BeTrue();
        score.ArgsCorrect.Should().Be(5);
        score.TaskSuccess.Should().BeTrue();
    }

    [Fact]
    public void WrongDate_CountsAsArgumentError()
    {
        var scenario = Scenario("search_available_cars", new { pickupDate = "2026-11-23", returnDate = "2026-11-25" });
        var reply = Reply("ok", Call("search_available_cars", new { pickupDate = "2026-11-21", returnDate = "2026-11-25" }));

        var score = ScenarioScorer.Score(scenario, [reply]);

        score.ArgsCorrect.Should().Be(1);
        score.TaskSuccess.Should().BeFalse();
        score.ArgMismatches.Should().Contain("pickupDate");
    }

    [Fact]
    public void InterceptedCall_IsNotCountedAsExecuted_AndEffectiveArgsAreUsed()
    {
        var scenario = Scenario("search_available_cars", new { pickupDate = "2026-11-23", returnDate = "2026-11-25" });
        var reply = Reply("ok",
            Call("search_available_cars", new { pickupDate = "2026-11-21" }, success: false, intercepted: true, proposedInvalid: ["PickupDate: x"]),
            Call("search_available_cars", new { pickupDate = "2026-11-23" },
                effective: """{"pickupDate":"2026-11-23","returnDate":"2026-11-25"}"""));

        var score = ScenarioScorer.Score(scenario, [reply]);

        score.TaskSuccess.Should().BeTrue();
        score.InterceptedCalls.Should().Be(1);
        score.ProposedInvalidCalls.Should().Be(1);
        score.ExecutedInvalidCalls.Should().Be(0);
    }

    [Fact]
    public void BaselineCallWithInvalidArgs_IsExecutedInvalid()
    {
        var scenario = Scenario("search_available_cars", new { pickupDate = "2026-11-23", returnDate = "2026-11-25" });
        var reply = Reply("ok", Call("search_available_cars", new { pickupDate = "2026-11-21", returnDate = "2026-11-25" },
            proposedInvalid: ["PickupDate: weekday"]));

        ScenarioScorer.Score(scenario, [reply]).ExecutedInvalidCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("На які дати вам потрібне авто?", true)]
    [InlineData("З якого числа і до якого вам потрібна машина?", true)]
    [InlineData("When would you like to pick it up and return it?", true)]
    [InlineData("Будь ласка, вкажіть дати оренди (день отримання та день повернення).", true)]
    [InlineData("Вкажіть, будь ласка, дати початку та завершення оренди, щоб я міг підібрати авто.", true)]
    [InlineData("Please specify the rental dates.", true)]
    [InlineData("Будь ласка, вкажіть дату видачі та дату повернення авто.", true)]
    [InlineData("Яка дата початку та закінчення оренди?", true)]
    [InlineData("Будь ласка, зазначте дати: з якого дня по який?", true)]
    [InlineData("Підкажіть, будь ласка, коли забираєте і коли повертаєте авто?", true)]
    [InlineData("Вкажіть, будь ласка, місто.", false)]
    [InlineData("Яке місто вас цікавить?", false)]
    [InlineData("Ось вільні авто на ці дати.", false)]
    public void Clarification_RequiresQuestionOrRequestAboutMissingSlots(string text, bool expected)
    {
        var scenario = Scenario(null, asksFor: ["PickupDate", "ReturnDate"], mustNotCall: ["search_available_cars"]);

        ScenarioScorer.Score(scenario, [Reply(text)]).ClarificationCorrect.Should().Be(expected);
    }

    [Fact]
    public void Clarification_FailsWhenForbiddenToolRan()
    {
        var scenario = Scenario(null, asksFor: ["ReturnDate"], mustNotCall: ["search_available_cars"]);
        var reply = Reply("До якого числа?", Call("search_available_cars", new { pickupDate = "2026-12-20" }, success: false));

        var score = ScenarioScorer.Score(scenario, [reply]);

        score.ForbiddenCall.Should().BeTrue();
        score.ClarificationCorrect.Should().BeFalse();
    }

    [Fact]
    public void OptionalPreparatoryTool_DoesNotBreakToolSelection()
    {
        var scenario = Scenario("create_rental_draft", new { carId = 17 }, optional: ["get_car_details"]);
        var reply = Reply("Готово", Call("get_car_details", new { carId = 17 }), Call("create_rental_draft", new { carId = 17 }));

        ScenarioScorer.Score(scenario, [reply]).TaskSuccess.Should().BeTrue();
    }

    [Fact]
    public void AnyToolForbidden_FailsOnAnyExecutedCall()
    {
        var scenario = Scenario(null, mustNotCall: ["*"]);

        ScenarioScorer.Score(scenario, [Reply("Вибачте", Call("search_rental_policies", new { query = "x" }))]).TaskSuccess.Should().BeFalse();
        ScenarioScorer.Score(scenario, [Reply("Вибачте, лише оренда авто.")]).TaskSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("Депозит для SUV — 10 000 ₴.")]
    [InlineData("Депозит для SUV — 10 000 ₴.")]
    [InlineData("The deposit is 10,000 UAH.")]
    [InlineData("10000 грн")]
    public void MustContain_NormalizesDigitGroups(string text)
    {
        var scenario = Scenario("search_rental_policies", mustContain: ["10000"]);

        ScenarioScorer.Score(scenario, [Reply(text, Call("search_rental_policies", new { query = "депозит" }))]).ContentOk.Should().BeTrue();
    }

    [Fact]
    public void Summary_StdDevAndPercentile()
    {
        AgentSummary.StdDev([0.5, 0.7, 0.9]).Should().BeApproximately(0.2, 1e-9);
        AgentSummary.Percentile([100, 200, 300, 400, 500], 0.95).Should().BeApproximately(480, 1e-9);
    }

    [Fact]
    public void DailyLimitErrors_AreRecognized()
    {
        AgentExperiment.IsDailyLimit("Rate limit reached for model `x` on tokens per day (TPD): Limit 200000").Should().BeTrue();
        AgentExperiment.IsDailyLimit("Quota exceeded for metric: generate_content_free_tier_requests, GenerateRequestsPerDayPerProjectPerModel").Should().BeTrue();
        AgentExperiment.IsDailyLimit("Rate limit reached ... on tokens per minute (TPM)").Should().BeFalse();
    }

    [Fact]
    public void Bootstrap_IsDeterministicAndDetectsClearDifference()
    {
        var (low, high, p) = RetrievalExperiment.Bootstrap([0.5, 0.5, 0.5, 0.2, 0.3], 2000, seed: 1);

        low.Should().BeGreaterThan(0);
        high.Should().BeLessThanOrEqualTo(0.5);
        p.Should().Be(0);
    }
}
