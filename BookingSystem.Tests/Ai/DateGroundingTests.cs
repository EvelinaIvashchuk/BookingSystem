using System.Text.Json;
using BookingSystem.Ai.Slots;
using BookingSystem.Evaluation;
using BookingSystem.Tests.Evaluation;
using FluentAssertions;

namespace BookingSystem.Tests.Ai;

public class DateGroundingTests
{
    private static readonly DateOnly Today = new(2026, 11, 18); // Wednesday, as in the evaluation dataset

    private static bool Grounded(string date, bool isReturn, params string[] userMessages) =>
        DateGrounding.IsGrounded(DateOnly.Parse(date), isReturn, new DateGroundingContext(Today, userMessages));

    // ── Invented dates (E1 conclusion 5, E2 I-02) ───────────────────────────

    [Theory]
    [InlineData("I need a car from December 10", "2026-12-11")]
    [InlineData("Потрібен автомат з 20 грудня", "2026-12-21")]
    [InlineData("Хочу авто з 25 грудня", "2026-12-26")]
    [InlineData("Хочу дизель до 1500 грн на добу з 10 грудня", "2026-12-11")]
    [InlineData("Потрібен мінівен на 7 місць з 3 грудня", "2026-12-07")]
    [InlineData("Забронюй авто №12 з 10 грудня", "2026-12-12")]
    public void ReturnDate_NotStatedByUser_IsNotGrounded(string message, string invented)
    {
        Grounded(invented, isReturn: true, message).Should().BeFalse();
    }

    [Theory]
    [InlineData("Хочу мінівен на 5 днів", "2026-11-18")]
    [InlineData("Шукаю SUV до 25 грудня", "2026-12-20")]
    [InlineData("Потрібен кросовер на два тижні", "2026-11-19")]
    public void PickupDate_NotStatedByUser_IsNotGrounded(string message, string invented)
    {
        Grounded(invented, isReturn: false, message).Should().BeFalse();
    }

    // ── Stated dates ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("з 10 по 12 грудня", "2026-12-12")]
    [InlineData("10-12 грудня", "2026-12-12")]
    [InlineData("з 10.12 по 12.12", "2026-12-12")]
    [InlineData("from December 10th to December 12th", "2026-12-12")]
    [InlineData("з десятого по дванадцяте грудня", "2026-12-12")]
    [InlineData("з двадцять першого грудня", "2026-12-21")]
    [InlineData("З 20 грудня 2026 р. по 22 грудня 2026 р.", "2026-12-22")] // date picker in the widget
    [InlineData("до неділі", "2026-11-22")]
    [InlineData("з 10 грудня на 3 дні", "2026-12-13")]
    [InlineData("з 10 грудня на тиждень", "2026-12-17")]
    [InlineData("from December 10 for a week", "2026-12-17")]
    [InlineData("на вихідні", "2026-11-23")]
    [InlineData("до кінця місяця", "2026-11-30")]
    public void ReturnDate_StatedByUser_IsGrounded(string message, string date)
    {
        Grounded(date, isReturn: true, message).Should().BeTrue();
    }

    [Theory]
    [InlineData("завтра на 2 дні", "2026-11-19")]
    [InlineData("післязавтра на тиждень", "2026-11-20")]
    [InlineData("tomorrow for 3 days", "2026-11-19")]
    [InlineData("сьогодні до п'ятниці", "2026-11-18")]
    [InlineData("через 3 дні на тиждень", "2026-11-21")]
    [InlineData("з цієї пʼятниці на 3 дні", "2026-11-20")]
    [InlineData("наступного тижня на 3 дні", "2026-11-24")]
    public void PickupDate_StatedRelatively_IsGrounded(string message, string date)
    {
        Grounded(date, isReturn: false, message).Should().BeTrue();
    }

    [Fact]
    public void NumbersThatAreNotDays_DoNotGround()
    {
        Grounded("2026-12-05", isReturn: true, "5 місць, з 1 грудня").Should().BeFalse();
        Grounded("2026-12-15", isReturn: true, "до 1500 грн, з 1 грудня").Should().BeFalse();
        Grounded("2026-12-10", isReturn: true, "о 10:00, з 1 грудня").Should().BeFalse();
        Grounded("2026-12-17", isReturn: true, "авто №17 з 1 грудня").Should().BeFalse();
    }

    [Fact]
    public void EarlierUserMessage_GroundsDate()
    {
        Grounded("2026-12-15", isReturn: true, "з 10 по 15 грудня", "У Львові").Should().BeTrue();
    }

    [Fact]
    public void KnownValue_GroundsSameDate_AndShiftWordsAdjustIt()
    {
        var known = new DateGroundingContext(Today, ["А дизельний є?"], KnownValue: new DateOnly(2026, 12, 5));
        DateGrounding.IsGrounded(new DateOnly(2026, 12, 5), true, known).Should().BeTrue();
        DateGrounding.IsGrounded(new DateOnly(2026, 12, 6), true, known).Should().BeFalse();

        var shifted = known with { UserMessages = ["А якщо на день довше?"] };
        DateGrounding.IsGrounded(new DateOnly(2026, 12, 6), true, shifted).Should().BeTrue();
    }

    [Fact]
    public void DateProposedByAssistant_IsGroundedWhenUserAgrees()
    {
        var context = new DateGroundingContext(Today, ["Так, підходить"], "Можу запропонувати з 20 по 23 грудня. Підходить?");

        DateGrounding.IsGrounded(new DateOnly(2026, 12, 23), true, context).Should().BeTrue();
        DateGrounding.IsGrounded(new DateOnly(2026, 12, 24), true, context).Should().BeFalse();
    }

    // ── The evaluation dataset: no false blocks on expected calls ────────────

    public static TheoryData<string> ScenariosWithExpectedDates()
    {
        var data = new TheoryData<string>();
        foreach (var s in LoadScenarios().Where(s => s.Expected.Args.ContainsKey("pickupDate")))
            data.Add(s.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(ScenariosWithExpectedDates))]
    public void ExpectedDatesOfEveryScenario_AreGrounded(string id)
    {
        var scenario = LoadScenarios().Single(s => s.Id == id);
        var today = DateOnly.FromDateTime(DateTime.Parse(scenario.Now ?? "2026-11-18T10:00:00"));
        var context = new DateGroundingContext(today, scenario.Turns);

        foreach (var (argument, isReturn) in new[] { ("pickupDate", false), ("returnDate", true) })
        {
            var date = DateOnly.Parse(scenario.Expected.Args[argument].GetString()!);
            DateGrounding.IsGrounded(date, isReturn, context).Should().BeTrue($"{id}: {argument} {date} is stated in \"{string.Join(" | ", scenario.Turns)}\"");
        }
    }

    private static List<AgentScenario> LoadScenarios() =>
        Datasets.LoadScenarios(Path.Combine(RepoRoot.Find(), "BookingSystem.Evaluation", "datasets", "agent_scenarios.json")).Scenarios;
}
