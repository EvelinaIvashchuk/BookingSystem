using System.Globalization;
using System.Text.Json;
using BookingSystem.Ai.Dtos;
using BookingSystem.Data;
using BookingSystem.Enums;
using BookingSystem.Models;
using BookingSystem.Services;
using BookingSystem.Services.Dtos;
using BookingSystem.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookingSystem.Ai;

/// <summary>
/// Human-in-the-loop step: turns a booking draft into a real rental only when its owner
/// presses "Confirm". The rental itself is created by the existing <see cref="IRentalService"/>,
/// so all business rules, the Pending status and the e-mail notification stay in one place.
/// </summary>
public sealed class PendingActionService(
    IUnitOfWork uow,
    IRentalService rentalService,
    AgentClock clock,
    ILogger<PendingActionService> logger) : IPendingActionService
{
    internal static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PendingActionOutcome> ConfirmAsync(Guid actionId, string userId, CancellationToken ct = default)
    {
        var (action, rejection) = await LoadForDecisionAsync(actionId, userId, ct);
        if (rejection is not null) return rejection;

        var draft = JsonSerializer.Deserialize<RentalDraft>(action!.PayloadJson, PayloadJsonOptions);
        if (draft is null || !TryParseDate(draft.PickupDate, out var pickup) || !TryParseDate(draft.ReturnDate, out var ret))
        {
            logger.LogError("Pending action {ActionId} has an unreadable payload", actionId);
            return new PendingActionOutcome(PendingActionOutcomeStatus.Failed, "The booking draft is invalid.");
        }

        // Claim the action first: the concurrency token on Status guarantees that only one
        // of two simultaneous confirmations gets past this point.
        action.Status = PendingActionStatus.Confirmed;
        action.CompletedAt = clock.UtcNow;
        if (!await TryCommitAsync(ct))
            return new PendingActionOutcome(PendingActionOutcomeStatus.AlreadyProcessed);

        ServiceResult<Rental> result;
        try
        {
            result = await rentalService.CreateRentalAsync(
                userId, new CreateRentalDto(draft.CarId, pickup, ret, draft.Notes));
        }
        catch
        {
            await ReleaseAsync(action, ct);
            throw;
        }

        if (!result.IsSuccess)
        {
            await ReleaseAsync(action, ct);
            logger.LogInformation("Pending action {ActionId} rejected by rental rules: {Error}", actionId, result.Error);
            return new PendingActionOutcome(PendingActionOutcomeStatus.Failed, result.Error);
        }

        action.RentalId = result.Value!.Id;
        await AddSessionNoteAsync(action.SessionId,
            $"[Booking confirmed by the user: rental #{action.RentalId}, {draft.CarName}, " +
            $"{draft.PickupDate} → {draft.ReturnDate}, {draft.TotalPrice:0.##} UAH. " +
            "Status: Pending, awaiting administrator approval.]", ct);
        await uow.CommitAsync(ct);

        logger.LogInformation(
            "Pending action {ActionId} confirmed by user {UserId}: rental {RentalId} created",
            actionId, userId, action.RentalId);

        return new PendingActionOutcome(PendingActionOutcomeStatus.Confirmed, RentalId: action.RentalId);
    }

    public async Task<PendingActionOutcome> CancelAsync(Guid actionId, string userId, CancellationToken ct = default)
    {
        var (action, rejection) = await LoadForDecisionAsync(actionId, userId, ct);
        if (rejection is not null) return rejection;

        action!.Status = PendingActionStatus.Cancelled;
        action.CompletedAt = clock.UtcNow;

        var draft = JsonSerializer.Deserialize<RentalDraft>(action.PayloadJson, PayloadJsonOptions);
        await AddSessionNoteAsync(action.SessionId,
            $"[The user cancelled the booking draft: {draft?.CarName}, {draft?.PickupDate} → {draft?.ReturnDate}.]", ct);

        if (!await TryCommitAsync(ct))
            return new PendingActionOutcome(PendingActionOutcomeStatus.AlreadyProcessed);

        logger.LogInformation("Pending action {ActionId} cancelled by user {UserId}", actionId, userId);
        return new PendingActionOutcome(PendingActionOutcomeStatus.Cancelled);
    }

    /// <summary>Checks existence, ownership, state and expiry. Returns the action or a rejection.</summary>
    private async Task<(PendingAction? Action, PendingActionOutcome? Rejection)> LoadForDecisionAsync(
        Guid actionId, string userId, CancellationToken ct)
    {
        var action = await uow.PendingActions.GetAsync(actionId, ct);

        if (action is null)
            return (null, new PendingActionOutcome(PendingActionOutcomeStatus.NotFound));

        if (action.UserId != userId)
        {
            logger.LogWarning("User {UserId} tried to act on pending action {ActionId} of another user", userId, actionId);
            return (null, new PendingActionOutcome(PendingActionOutcomeStatus.Forbidden));
        }

        if (action.Status != PendingActionStatus.Pending)
            return (null, new PendingActionOutcome(PendingActionOutcomeStatus.AlreadyProcessed));

        if (clock.UtcNow > action.ExpiresAt)
        {
            action.Status = PendingActionStatus.Expired;
            action.CompletedAt = clock.UtcNow;
            await TryCommitAsync(ct);
            return (null, new PendingActionOutcome(PendingActionOutcomeStatus.Expired));
        }

        return (action, null);
    }

    /// <summary>Returns the action to Pending when the booking could not be created.</summary>
    private async Task ReleaseAsync(PendingAction action, CancellationToken ct)
    {
        action.Status = PendingActionStatus.Pending;
        action.CompletedAt = null;
        await TryCommitAsync(ct);
    }

    private async Task<bool> TryCommitAsync(CancellationToken ct)
    {
        try
        {
            await uow.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    /// <summary>
    /// Leaves a note in the chat history so the model knows on the next turn what the user decided.
    /// </summary>
    private Task AddSessionNoteAsync(Guid sessionId, string text, CancellationToken ct) =>
        uow.ChatSessions.AddMessagesAsync(
        [
            new ChatSessionMessage
            {
                SessionId = sessionId,
                Role = ChatSessionMessage.AssistantRole,
                Content = text,
                CreatedAt = clock.UtcNow
            }
        ], ct);

    private static bool TryParseDate(string value, out DateTime date) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
