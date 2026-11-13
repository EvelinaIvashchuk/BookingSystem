using System.ClientModel;
using System.Diagnostics;
using BookingSystem.Ai.Dtos;
using BookingSystem.Data;
using BookingSystem.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace BookingSystem.Ai;

/// <summary>
/// Runs one chat turn through a <see cref="ChatClientAgent"/> built on top of the configured model.
/// The function-invocation loop is assembled here (not by the agent's defaults) so that the
/// iteration limit and, later, slot validation are under our control.
/// Conversation history lives in the database and the last <see cref="AiOptions.HistoryLength"/>
/// messages are replayed to the model on every turn.
/// </summary>
public sealed class AgentService(
    IChatClientFactory chatClientFactory,
    IUnitOfWork uow,
    AgentClock clock,
    IOptions<AiOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<AgentService> logger) : IAgentService
{
    internal const string NotConfiguredText = "AI service is not configured.";
    internal const string UnavailableText = "AI service is unavailable. Please try again later.";

    private readonly AiOptions _options = options.Value;

    public async Task<AgentReply> ChatAsync(
        Guid sessionId,
        string? userId,
        string message,
        AgentChatOptions? chatOptions = null,
        CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(chatOptions?.Model) ? _options.DefaultModel : chatOptions.Model!;

        var session = await ResolveSessionAsync(sessionId, userId, cancellationToken);
        var history = await uow.ChatSessions.GetRecentMessagesAsync(session.Id, _options.HistoryLength, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var userMessageAt = clock.UtcNow;

        try
        {
            var agent = CreateAgent(model, isAuthenticated: userId is not null, tools: []);

            List<AiChatMessage> messages = [.. history.Select(ToAiMessage), new(ChatRole.User, message)];

            var response = await agent.RunAsync(messages, session: null, options: null, cancellationToken);

            stopwatch.Stop();

            var reply = new AgentReply
            {
                Text = response.Text.Trim(),
                SessionId = session.Id,
                Model = model,
                InputTokens = ToInt(response.Usage?.InputTokenCount),
                OutputTokens = ToInt(response.Usage?.OutputTokenCount),
                LatencyMs = stopwatch.ElapsedMilliseconds
            };

            await SaveTurnAsync(session, message, userMessageAt, reply, cancellationToken);

            logger.LogInformation(
                "AI turn completed: session {SessionId}, model {Model}, history {HistoryCount}, {InputTokens} in / {OutputTokens} out tokens, {LatencyMs} ms",
                session.Id, model, history.Count, reply.InputTokens, reply.OutputTokens, reply.LatencyMs);

            return reply;
        }
        catch (AiNotConfiguredException ex)
        {
            logger.LogWarning("AI turn skipped: {Reason}", ex.Message);
            return Degraded(NotConfiguredText, session.Id, model, stopwatch);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            logger.LogError(ex, "AI provider call failed for session {SessionId}, model {Model}", session.Id, model);
            return Degraded(UnavailableText, session.Id, model, stopwatch);
        }
    }

    /// <summary>
    /// Returns the requested session if the caller may use it, otherwise starts a new one.
    /// An anonymous session is adopted by the user who signs in during the conversation;
    /// a session that belongs to a user is never shown to anyone else (including after sign-out).
    /// New sessions are committed right away so tools can reference them.
    /// </summary>
    private async Task<ChatSession> ResolveSessionAsync(Guid requestedId, string? userId, CancellationToken ct)
    {
        var existing = requestedId == Guid.Empty
            ? null
            : await uow.ChatSessions.GetAsync(requestedId, ct);

        if (existing is not null)
        {
            if (existing.UserId == userId)
                return existing;

            if (existing.UserId is null)
            {
                existing.UserId = userId;
                existing.UpdatedAt = clock.UtcNow;
                await uow.CommitAsync(ct);
                return existing;
            }

            logger.LogWarning(
                "Chat session {SessionId} belongs to another user; starting a new session", requestedId);
        }

        var now = clock.UtcNow;
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await uow.ChatSessions.AddAsync(session, ct);
        await uow.CommitAsync(ct);
        return session;
    }

    private async Task SaveTurnAsync(
        ChatSession session, string userText, DateTime userMessageAt, AgentReply reply, CancellationToken ct)
    {
        var now = clock.UtcNow;

        await uow.ChatSessions.AddMessagesAsync(
        [
            new ChatSessionMessage
            {
                SessionId = session.Id,
                Role = ChatSessionMessage.UserRole,
                Content = userText,
                CreatedAt = userMessageAt
            },
            new ChatSessionMessage
            {
                SessionId = session.Id,
                Role = ChatSessionMessage.AssistantRole,
                Content = reply.Text,
                CreatedAt = now,
                Model = reply.Model,
                InputTokens = reply.InputTokens,
                OutputTokens = reply.OutputTokens,
                LatencyMs = (int)Math.Min(reply.LatencyMs, int.MaxValue),
                ToolCallsJson = reply.ToolCalls.Count == 0
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(reply.ToolCalls)
            }
        ], ct);

        session.UpdatedAt = now;
        await uow.CommitAsync(ct);
    }

    private static AiChatMessage ToAiMessage(ChatSessionMessage message) => new(
        message.Role == ChatSessionMessage.AssistantRole ? ChatRole.Assistant : ChatRole.User,
        message.Content);

    private ChatClientAgent CreateAgent(string model, bool isAuthenticated, IList<AITool> tools)
    {
        var chatClient = chatClientFactory.Create(model)
            .AsBuilder()
            .UseFunctionInvocation(loggerFactory, invoking =>
            {
                invoking.MaximumIterationsPerRequest = _options.MaxAgentIterations;
                invoking.MaximumConsecutiveErrorsPerRequest = 2;
            })
            .Build();

        return new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = "CarRentalAssistant",
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions
                {
                    Instructions = AgentPrompts.BuildInstructions(clock.Now, isAuthenticated),
                    Temperature = _options.Temperature,
                    MaxOutputTokens = _options.MaxOutputTokens,
                    Tools = tools
                }
            },
            loggerFactory);
    }

    private static bool IsProviderFailure(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        ClientResultException => true,                                        // HTTP 4xx/5xx from provider
        HttpRequestException => true,                                         // network failure
        TaskCanceledException or OperationCanceledException
            => !cancellationToken.IsCancellationRequested,                    // provider timeout
        _ => false
    };

    private static AgentReply Degraded(string text, Guid sessionId, string model, Stopwatch stopwatch) => new()
    {
        Text = text,
        SessionId = sessionId,
        Model = model,
        IsDegraded = true,
        LatencyMs = stopwatch.ElapsedMilliseconds
    };

    private static int? ToInt(long? value) => value is null ? null : (int)Math.Min(value.Value, int.MaxValue);
}
