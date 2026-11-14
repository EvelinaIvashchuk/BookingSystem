using System.Globalization;
using System.Text.Json;
using BookingSystem.Ai;
using BookingSystem.Ai.Dtos;
using BookingSystem.Data;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services;
using BookingSystem.Services.Interfaces;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BookingSystem.Tests.Ai;

/// <summary>
/// Confirmation of agent drafts goes through the real <see cref="RentalService"/> (InMemory DB),
/// so the dates are relative to the real current date — RentalService validates against it.
/// </summary>
public class PendingActionServiceTests : IAsyncLifetime
{
    private readonly ApplicationDbContext _db = TestDbContextFactory.Create();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly Guid _sessionId = Guid.NewGuid();

    private DateTime Pickup => _now.UtcDateTime.Date.AddDays(10);
    private DateTime Return => _now.UtcDateTime.Date.AddDays(13);

    public async Task InitializeAsync()
    {
        await AiTestData.SeedAsync(_db);
        _db.ChatSessions.Add(new ChatSession { Id = _sessionId, UserId = "user-1" });
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private PendingActionService CreateSut(DateTimeOffset? now = null)
    {
        var uow = AiTestData.CreateUnitOfWork(_db);
        var rentalService = new RentalService(uow, Mock.Of<IEmailService>(), NullLogger<RentalService>.Instance);
        return new PendingActionService(uow, rentalService, AiTestData.CreateClock(now ?? _now),
            NullLogger<PendingActionService>.Instance);
    }

    private async Task<PendingAction> AddDraftAsync(string userId = "user-1", int carId = AiTestData.CrvId)
    {
        var id = Guid.NewGuid();
        var draft = new RentalDraft
        {
            PendingActionId = id,
            CarId = carId,
            CarName = "Honda CR-V (2023)",
            PickupDate = Pickup.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReturnDate = Return.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Days = 3,
            PricePerDay = 1800,
            TotalPrice = 5400,
            ExpiresAt = _now.UtcDateTime.AddMinutes(15)
        };
        var action = new PendingAction
        {
            Id = id,
            SessionId = _sessionId,
            UserId = userId,
            PayloadJson = JsonSerializer.Serialize(draft, PendingActionService.PayloadJsonOptions),
            Status = PendingActionStatus.Pending,
            CreatedAt = _now.UtcDateTime,
            ExpiresAt = draft.ExpiresAt
        };
        _db.PendingActions.Add(action);
        await _db.SaveChangesAsync();
        return action;
    }

    private Task<List<Rental>> NewRentalsAsync() =>
        _db.Rentals.Where(r => r.CarId == AiTestData.CrvId).ToListAsync();

    [Fact]
    public async Task Confirm_OwnDraft_CreatesPendingRentalThroughRentalService()
    {
        var action = await AddDraftAsync();

        var outcome = await CreateSut().ConfirmAsync(action.Id, "user-1");

        outcome.Status.Should().Be(PendingActionOutcomeStatus.Confirmed);
        var rental = (await NewRentalsAsync()).Should().ContainSingle().Subject;
        rental.Id.Should().Be(outcome.RentalId);
        rental.UserId.Should().Be("user-1");
        rental.Status.Should().Be(RentalStatus.Pending);
        rental.TotalPrice.Should().Be(1800 * 3);

        var stored = await _db.PendingActions.SingleAsync();
        stored.Status.Should().Be(PendingActionStatus.Confirmed);
        stored.RentalId.Should().Be(rental.Id);
        stored.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Confirm_LeavesNoteInChatHistoryForTheModel()
    {
        var action = await AddDraftAsync();

        var outcome = await CreateSut().ConfirmAsync(action.Id, "user-1");

        var note = await _db.ChatMessages.SingleAsync(m => m.SessionId == _sessionId);
        note.Content.Should().Contain("Booking confirmed").And.Contain($"#{outcome.RentalId}");
    }

    [Fact]
    public async Task Confirm_DraftOfAnotherUser_IsForbiddenAndCreatesNothing()
    {
        var action = await AddDraftAsync(userId: "other-user");

        var outcome = await CreateSut().ConfirmAsync(action.Id, "user-1");

        outcome.Status.Should().Be(PendingActionOutcomeStatus.Forbidden);
        (await NewRentalsAsync()).Should().BeEmpty();
        (await _db.PendingActions.SingleAsync()).Status.Should().Be(PendingActionStatus.Pending);
    }

    [Fact]
    public async Task Confirm_ExpiredDraft_IsRejectedAndMarkedExpired()
    {
        var action = await AddDraftAsync();

        var outcome = await CreateSut(now: _now.AddMinutes(16)).ConfirmAsync(action.Id, "user-1");

        outcome.Status.Should().Be(PendingActionOutcomeStatus.Expired);
        (await NewRentalsAsync()).Should().BeEmpty();
        (await _db.PendingActions.SingleAsync()).Status.Should().Be(PendingActionStatus.Expired);
    }

    [Fact]
    public async Task Confirm_Twice_CreatesOnlyOneRental()
    {
        var action = await AddDraftAsync();
        var sut = CreateSut();

        await sut.ConfirmAsync(action.Id, "user-1");
        var second = await sut.ConfirmAsync(action.Id, "user-1");

        second.Status.Should().Be(PendingActionOutcomeStatus.AlreadyProcessed);
        (await NewRentalsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Cancel_ThenConfirm_CreatesNothing()
    {
        var action = await AddDraftAsync();
        var sut = CreateSut();

        var cancelled = await sut.CancelAsync(action.Id, "user-1");
        var confirmed = await sut.ConfirmAsync(action.Id, "user-1");

        cancelled.Status.Should().Be(PendingActionOutcomeStatus.Cancelled);
        confirmed.Status.Should().Be(PendingActionOutcomeStatus.AlreadyProcessed);
        (await NewRentalsAsync()).Should().BeEmpty();
        (await _db.PendingActions.SingleAsync()).Status.Should().Be(PendingActionStatus.Cancelled);
    }

    [Fact]
    public async Task Cancel_DraftOfAnotherUser_IsForbidden()
    {
        var action = await AddDraftAsync(userId: "other-user");

        var outcome = await CreateSut().CancelAsync(action.Id, "user-1");

        outcome.Status.Should().Be(PendingActionOutcomeStatus.Forbidden);
        (await _db.PendingActions.SingleAsync()).Status.Should().Be(PendingActionStatus.Pending);
    }

    [Fact]
    public async Task Confirm_WhenCarWasBookedMeanwhile_FailsAndKeepsDraftPending()
    {
        var action = await AddDraftAsync();
        _db.Rentals.Add(new Rental
        {
            UserId = "other-user", CarId = AiTestData.CrvId, Status = RentalStatus.Confirmed,
            PickupDate = Pickup.AddDays(1), ReturnDate = Return.AddDays(1)
        });
        await _db.SaveChangesAsync();

        var outcome = await CreateSut().ConfirmAsync(action.Id, "user-1");

        outcome.Status.Should().Be(PendingActionOutcomeStatus.Failed);
        outcome.Error.Should().Contain("already rented");
        (await _db.PendingActions.SingleAsync()).Status.Should().Be(PendingActionStatus.Pending);
        (await NewRentalsAsync()).Should().ContainSingle(r => r.UserId == "other-user");
    }

    [Fact]
    public async Task Confirm_UnknownId_ReturnsNotFound()
    {
        var outcome = await CreateSut().ConfirmAsync(Guid.NewGuid(), "user-1");

        outcome.Status.Should().Be(PendingActionOutcomeStatus.NotFound);
    }

    [Fact]
    public void Status_IsConcurrencyToken_SoParallelConfirmsCannotBothSucceed()
    {
        var property = _db.Model.FindEntityType(typeof(PendingAction))!.FindProperty(nameof(PendingAction.Status))!;

        property.IsConcurrencyToken.Should().BeTrue();
    }
}
