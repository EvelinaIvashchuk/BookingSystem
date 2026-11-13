namespace BookingSystem.Models;

/// <summary>
/// One message of a <see cref="ChatSession"/> plus per-turn telemetry used in the experiments.
/// </summary>
public class ChatSessionMessage
{
    public const string UserRole = "user";
    public const string AssistantRole = "assistant";

    public long Id { get; set; }

    public Guid SessionId { get; set; }
    public ChatSession Session { get; set; } = null!;

    /// <summary><see cref="UserRole"/> or <see cref="AssistantRole"/>.</summary>
    public string Role { get; set; } = UserRole;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Tool calls made while producing this assistant message (JSON array).</summary>
    public string? ToolCallsJson { get; set; }

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? LatencyMs { get; set; }

    /// <summary>Model that produced the message, in <c>provider:model</c> form.</summary>
    public string? Model { get; set; }
}
