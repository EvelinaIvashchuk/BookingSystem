using BookingSystem.Ai.Slots;
using FluentAssertions;

namespace BookingSystem.Tests.Ai;

public class DateExpressionHintsTests
{
    [Theory]
    [InlineData("Авто з цієї п'ятниці", DayOfWeek.Friday)]
    [InlineData("з пʼятниці на 3 дні", DayOfWeek.Friday)]          // U+02BC apostrophe
    [InlineData("з пятниці", DayOfWeek.Friday)]                      // no apostrophe
    [InlineData("до неділі", DayOfWeek.Sunday)]
    [InlineData("У середу зранку", DayOfWeek.Wednesday)]
    [InlineData("з понеділка", DayOfWeek.Monday)]
    [InlineData("в суботу", DayOfWeek.Saturday)]
    [InlineData("I need a car from next Monday", DayOfWeek.Monday)]
    [InlineData("pick up on Thursday", DayOfWeek.Thursday)]
    public void MentionedWeekdays_FindsInflectedForms(string text, DayOfWeek expected)
    {
        DateExpressionHints.MentionedWeekdays(text).Should().Equal(expected);
    }

    [Fact]
    public void MentionedWeekdays_FindsSeveral()
    {
        DateExpressionHints.MentionedWeekdays("з понеділка по середу")
            .Should().BeEquivalentTo([DayOfWeek.Monday, DayOfWeek.Wednesday]);
    }

    [Theory]
    [InlineData("Який серед них найдешевший?")]   // "серед" = among
    [InlineData("з 20 по 25 грудня")]
    [InlineData("")]
    public void MentionedWeekdays_NoFalsePositives(string text)
    {
        DateExpressionHints.MentionedWeekdays(text).Should().BeEmpty();
    }

    [Theory]
    [InlineData("на 3 дні", 3)]
    [InlineData("на три дні", 3)]
    [InlineData("на 5 днів", 5)]
    [InlineData("на один день", 1)]
    [InlineData("на 2 доби", 2)]
    [InlineData("на пʼять днів", 5)]
    [InlineData("for 5 days", 5)]
    [InlineData("for a day", 1)]
    [InlineData("на тиждень", 7)]
    [InlineData("на два тижні", 14)]
    [InlineData("for two weeks", 14)]
    public void MentionedDurationDays_ParsesExplicitDurations(string text, int expected)
    {
        DateExpressionHints.MentionedDurationDays(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("через 3 дні")]                 // start date, not a duration
    [InlineData("in 3 days")]
    [InlineData("автомат на 5 місць")]
    [InlineData("з 20 по 25 грудня")]
    [InlineData("наступного тижня")]
    [InlineData("на 3 дні або на 5 днів")]      // conflicting
    public void MentionedDurationDays_IgnoresNonDurations(string text)
    {
        DateExpressionHints.MentionedDurationDays(text).Should().BeNull();
    }
}
