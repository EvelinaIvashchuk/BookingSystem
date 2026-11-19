using BookingSystem.Ai;
using BookingSystem.Ai.Dtos;
using BookingSystem.Ai.Slots;
using BookingSystem.Ai.Tools;
using FluentAssertions;

namespace BookingSystem.Tests.Ai;

public class DateClarificationTests
{
    private const string Both = "PickupDate,ReturnDate";

    [Theory]
    [InlineData("Будь ласка, вкажіть дати оренди (день отримання та день повернення).", Both)]
    [InlineData("Вкажіть, будь ласка, дати початку та завершення оренди, щоб я міг підібрати авто.", Both)]
    [InlineData("Підкажіть, будь ласка, дати: коли забираєте і коли повертаєте авто?", Both)]
    [InlineData("Which dates do you need the car for?", Both)]
    [InlineData("Будь ласка, уточніть дату повернення автомобіля.", "ReturnDate")]
    [InlineData("На скільки днів вам потрібне авто?", "ReturnDate")]
    [InlineData("Please let me know when you would like to return the car.", "ReturnDate")]
    [InlineData("Будь ласка, вкажіть дату, коли ви плануєте забрати автомобіль.", "PickupDate")]
    [InlineData("Підкажіть, будь ласка, місто видачі авто?", "")]
    [InlineData("Яке місто вас цікавить?", "")]
    [InlineData("Ось вільні авто на ці дати.", "")]
    [InlineData("Дата повернення — 22 грудня, загальна вартість 3 400 ₴.", "")]
    public void AskedDateSlots_DetectsWhichDatesTheReplyAsksFor(string reply, string expected) =>
        string.Join(',', DateClarification.AskedDateSlots(reply)).Should().Be(expected);

    private const string AskBoth = "Вкажіть, будь ласка, дати оренди.";

    private static AgentTurnContext Turn(BookingSlots? slots = null) =>
        new(Guid.NewGuid(), "user-1", new DateOnly(2026, 11, 18), "Хочу авто", slots);

    [Fact]
    public void DatesToAsk_SkipsDatesAlreadyKnownInTheSession()
    {
        var turn = Turn(new BookingSlots { PickupDate = new DateOnly(2026, 12, 20) });

        DateClarification.DatesToAsk(turn, AskBoth).Should().Equal(SlotNames.ReturnDate);
    }

    [Fact]
    public void DatesToAsk_NothingWhenCarsWereShown()
    {
        var turn = Turn();
        turn.Cars = [new CarCardDto(1, "Toyota Camry", 1200, 2400, null)];

        DateClarification.DatesToAsk(turn, "Бажаєте змінити дати?").Should().BeEmpty();
    }

    [Fact]
    public void DatesToAsk_NothingAfterAKnowledgeBaseAnswer()
    {
        var turn = Turn();
        turn.ToolCalls.Add(new ToolCallRecord(BookingTools.SearchRentalPoliciesName, "{}", true, 5));

        DateClarification.DatesToAsk(turn, "Повернути авто можна до 20:00 дати повернення. Ще питання?").Should().BeEmpty();
    }

    [Fact]
    public void DatesToAsk_UsesTheDatesABlockedCallWasMissing()
    {
        var turn = Turn();
        turn.MissingSlots = [SlotNames.ReturnDate, SlotNames.Location];

        DateClarification.DatesToAsk(turn, "Яке місто?").Should().Equal(SlotNames.ReturnDate);
    }
}
