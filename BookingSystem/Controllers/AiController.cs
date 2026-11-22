using System.Security.Claims;
using BookingSystem.Ai;
using BookingSystem.Helpers;
using BookingSystem.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace BookingSystem.Controllers;

/// <summary>Thin HTTP layer over <see cref="IAgentService"/> and <see cref="IPendingActionService"/>.</summary>
public class AiController(
    IAgentService agentService,
    IPendingActionService pendingActions,
    IOptions<AiOptions> aiOptions,
    IStringLocalizer<SharedResources> localizer) : Controller
{
    // POST /Ai/Chat — rate limited (see Security/AiRateLimiting); the antiforgery token
    // stops other sites from driving the assistant with a signed-in user's cookie.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Chat([FromBody] ChatRequest? request, CancellationToken cancellationToken)
    {
        var message = request?.Message?.Trim();
        if (string.IsNullOrEmpty(message))
            return BadRequest();

        if (message.Length > aiOptions.Value.MaxMessageLength)
            return BadRequest(new { reply = $"Message is too long (max {aiOptions.Value.MaxMessageLength} characters)." });

        // The user id always comes from the auth cookie, never from the model or the request body.
        var userId = User.Identity?.IsAuthenticated == true
            ? User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        var reply = await agentService.ChatAsync(
            request!.SessionId ?? Guid.Empty, userId, message, cancellationToken: cancellationToken);

        var body = new
        {
            reply = reply.Text,
            sessionId = reply.SessionId,
            // ImageUrl may be an S3 object key; resolve it like the catalog views do.
            cars = reply.Cars.Select(c => c with { ImageUrl = NullIfEmpty(ImageHelper.Src(c.ImageUrl)) }),
            pendingActionId = reply.PendingActionId,
            draft = reply.Draft,
            missingSlots = reply.MissingSlots,
            askedDates = reply.AskedDates
        };

        return reply.IsDegraded ? StatusCode(StatusCodes.Status503ServiceUnavailable, body) : Ok(body);
    }

    // GET /Ai/History?sessionId=... — restores the conversation after navigation.
    [HttpGet]
    public async Task<IActionResult> History(Guid sessionId, CancellationToken cancellationToken)
    {
        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var history = await agentService.GetHistoryAsync(sessionId, userId, cancellationToken);
        if (history is null) return NotFound();

        return Ok(new
        {
            sessionId = history.SessionId,
            messages = history.Messages.Select(m => new { role = m.Role, text = m.Content }),
            draft = history.PendingDraft
        });
    }

    // POST /Ai/Confirm/{id} — the only way a booking prepared by the agent becomes a rental.
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var outcome = await pendingActions.ConfirmAsync(id, userId, cancellationToken);
        return ToActionResult(outcome);
    }

    // POST /Ai/Cancel/{id}
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Challenge();

        var outcome = await pendingActions.CancelAsync(id, userId, cancellationToken);
        return ToActionResult(outcome);
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private IActionResult ToActionResult(PendingActionOutcome outcome) => outcome.Status switch
    {
        PendingActionOutcomeStatus.Confirmed => Ok(new
        {
            ok = true,
            message = string.Format(localizer["Chat_BookingConfirmed"].Value, outcome.RentalId),
            rentalId = outcome.RentalId,
            url = Url.Action("MyRentals", "Rental")
        }),
        PendingActionOutcomeStatus.Cancelled =>
            Ok(new { ok = true, message = localizer["Chat_BookingCancelled"].Value }),
        PendingActionOutcomeStatus.NotFound =>
            NotFound(new { ok = false, message = localizer["Chat_DraftNotFound"].Value }),
        PendingActionOutcomeStatus.Forbidden =>
            StatusCode(StatusCodes.Status403Forbidden, new { ok = false, message = localizer["Chat_DraftForbidden"].Value }),
        PendingActionOutcomeStatus.Expired =>
            StatusCode(StatusCodes.Status410Gone, new { ok = false, message = localizer["Chat_DraftExpired"].Value }),
        PendingActionOutcomeStatus.AlreadyProcessed =>
            Conflict(new { ok = false, message = localizer["Chat_DraftProcessed"].Value }),
        _ => UnprocessableEntity(new
        {
            ok = false,
            message = string.Format(localizer["Chat_BookingFailed"].Value, outcome.Error)
        })
    };
}

public sealed class ChatRequest
{
    public string? Message { get; set; }
    public Guid? SessionId { get; set; }
}
