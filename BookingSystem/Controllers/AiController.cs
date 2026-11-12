using System.Security.Claims;
using BookingSystem.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BookingSystem.Controllers;

/// <summary>Thin HTTP layer over <see cref="IAgentService"/>.</summary>
public class AiController(IAgentService agentService, IOptions<AiOptions> aiOptions) : Controller
{
    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest? request, CancellationToken cancellationToken)
    {
        var message = request?.Message?.Trim();
        if (string.IsNullOrEmpty(message))
            return BadRequest();

        if (message.Length > aiOptions.Value.MaxMessageLength)
            return BadRequest(new { reply = $"Message is too long (max {aiOptions.Value.MaxMessageLength} characters)." });

        var sessionId = request!.SessionId ?? Guid.NewGuid();

        // The user id always comes from the auth cookie, never from the model or the request body.
        var userId = User.Identity?.IsAuthenticated == true
            ? User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        var reply = await agentService.ChatAsync(sessionId, userId, message, cancellationToken: cancellationToken);

        var body = new
        {
            reply = reply.Text,
            sessionId,
            cars = reply.Cars,
            pendingActionId = reply.PendingActionId,
            missingSlots = reply.MissingSlots
        };

        return reply.IsDegraded ? StatusCode(StatusCodes.Status503ServiceUnavailable, body) : Ok(body);
    }
}

public sealed class ChatRequest
{
    public string? Message { get; set; }
    public Guid? SessionId { get; set; }
}
