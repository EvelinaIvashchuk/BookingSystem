using BookingSystem.Ai.Dtos;

namespace BookingSystem.Ai;

public interface IAgentService
{
    /// <summary>Processes one user turn of a chat session.</summary>
    /// <param name="sessionId">Chat session identifier (kept by the widget).</param>
    /// <param name="userId">Authenticated user id taken from claims; <c>null</c> for anonymous users.</param>
    /// <param name="message">User message.</param>
    /// <param name="options">Per-call overrides (used by experiments).</param>
    Task<AgentReply> ChatAsync(
        Guid sessionId,
        string? userId,
        string message,
        AgentChatOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Per-call overrides for <see cref="IAgentService.ChatAsync"/>.</summary>
public sealed class AgentChatOptions
{
    /// <summary>Model in <c>provider:model</c> form; <c>null</c> means the configured default.</summary>
    public string? Model { get; init; }

    /// <summary>Overrides <c>Ai:SlotFilling:Enabled</c> for this call (ablation experiments).</summary>
    public bool? SlotFillingEnabled { get; init; }
}
