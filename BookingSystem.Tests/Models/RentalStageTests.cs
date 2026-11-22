using BookingSystem.Enums;
using BookingSystem.Models;
using FluentAssertions;

namespace BookingSystem.Tests.Models;

/// <summary>
/// Тести <see cref="Rental.GetStage"/>: статус для показу користувачу з урахуванням дат
/// (підтверджена оренда стає «Триває» в день отримання і «Завершено» після дня повернення).
/// </summary>
public class RentalStageTests
{
    private static readonly DateTime Today = new(2026, 11, 22);

    private static Rental MakeRental(RentalStatus status, int pickupOffset, int returnOffset) => new()
    {
        Status     = status,
        PickupDate = Today.AddDays(pickupOffset),
        ReturnDate = Today.AddDays(returnOffset)
    };

    [Theory]
    [InlineData(3, 6, RentalStage.Confirmed)]   // ще не почалась
    [InlineData(0, 3, RentalStage.Active)]      // день отримання
    [InlineData(-2, 1, RentalStage.Active)]     // посередині
    [InlineData(-3, 0, RentalStage.Active)]     // день повернення
    [InlineData(-5, -1, RentalStage.Completed)] // повернення було вчора
    public void Confirmed_DependsOnDates(int pickupOffset, int returnOffset, RentalStage expected)
    {
        MakeRental(RentalStatus.Confirmed, pickupOffset, returnOffset).GetStage(Today).Should().Be(expected);
    }

    [Theory]
    [InlineData(RentalStatus.Pending, RentalStage.Pending)]
    [InlineData(RentalStatus.Cancelled, RentalStage.Cancelled)]
    [InlineData(RentalStatus.Rejected, RentalStage.Rejected)]
    public void OtherStatuses_DoNotChangeWithDates(RentalStatus status, RentalStage expected)
    {
        MakeRental(status, -5, -1).GetStage(Today).Should().Be(expected);
        MakeRental(status, 0, 3).GetStage(Today).Should().Be(expected);
        MakeRental(status, 3, 6).GetStage(Today).Should().Be(expected);
    }

    [Fact]
    public void TimeOfDayIsIgnored()
    {
        var rental = MakeRental(RentalStatus.Confirmed, -3, 0);

        rental.GetStage(Today.AddHours(23).AddMinutes(59)).Should().Be(RentalStage.Active);
        rental.GetStage(Today.AddDays(1)).Should().Be(RentalStage.Completed);
    }
}
