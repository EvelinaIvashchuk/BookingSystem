using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using BookingSystem.Ai.Dtos;
using BookingSystem.Ai.Tools;
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
    BookingToolsFactory toolsFactory,
    AgentClock clock,
    IOptions<AiOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<AgentService> logger) : IAgentService
{
    internal const string NotConfiguredText = "AI service is not configured.";
    internal const string UnavailableText = "AI service is unavailable. Please try again later.";
    internal const string EmptyReplyText =
        "Вибачте, не вдалося обробити запит. Спробуйте сформулювати його інакше. / " +
        "Sorry, I couldn't complete this request. Please try rephrasing it.";

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
            var turn = new AgentTurnContext(session.Id, userId, clock.Today);
            var agent = CreateAgent(model, turn);

            List<AiChatMessage> messages = [.. history.Select(ToAiMessage), new(ChatRole.User, message)];

            var response = await agent.RunAsync(messages, session: null, options: null, cancellationToken);

            stopwatch.Stop();

            var text = response.Text.Trim();
            if (text.Length == 0)
            {
                // Iteration limit reached or the model returned only tool calls.
                logger.LogWarning(
                    "Model {Model} produced no text for session {SessionId} after {ToolCalls} tool call(s)",
                    model, session.Id, turn.ToolCalls.Count);
                text = EmptyReplyText;
            }

            var reply = new AgentReply
            {
                Text = text,
                SessionId = session.Id,
                Cars = turn.Cars,
                ToolCalls = [.. turn.ToolCalls],
                Model = model,
                InputTokens = ToInt(response.Usage?.InputTokenCount),
                OutputTokens = ToInt(response.Usage?.OutputTokenCount),
                LatencyMs = stopwatch.ElapsedMilliseconds
            };

            await SaveTurnAsync(session, message, userMessageAt, reply, cancellationToken);

            logger.LogInformation(
                "AI turn completed: session {SessionId}, model {Model}, history {HistoryCount}, tools [{Tools}], {InputTokens} in / {OutputTokens} out tokens, {LatencyMs} ms",
                session.Id, model, history.Count, string.Join(", ", reply.ToolCalls.Select(t => t.Name)),
                reply.InputTokens, reply.OutputTokens, reply.LatencyMs);

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
                    : JsonSerializer.Serialize(reply.ToolCalls)
            }
        ], ct);

        session.UpdatedAt = now;
        await uow.CommitAsync(ct);
    }

    private static AiChatMessage ToAiMessage(ChatSessionMessage message) => new(
        message.Role == ChatSessionMessage.AssistantRole ? ChatRole.Assistant : ChatRole.User,
        message.Content);

    private ChatClientAgent CreateAgent(string model, AgentTurnContext turn)
    {
        var tools = toolsFactory.Create(turn).AsAITools();

        var chatClient = chatClientFactory.Create(model)
            .AsBuilder()
            .UseFunctionInvocation(loggerFactory, invoking =>
            {
                invoking.MaximumIterationsPerRequest = _options.MaxAgentIterations;
                invoking.MaximumConsecutiveErrorsPerRequest = 2;
                invoking.AllowConcurrentInvocation = false;   // tools share one DbContext
                invoking.FunctionInvoker = (context, ct) => InvokeToolAsync(turn, context, ct);
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
                    Instructions = AgentPrompts.BuildInstructions(clock.Now, isAuthenticated: turn.UserId is not null),
                    Temperature = _options.Temperature,
                    MaxOutputTokens = _options.MaxOutputTokens,
                    Tools = tools
                }
            },
            loggerFactory);
    }

    /// <summary>
    /// Single entry point for every tool call the model makes: measures and logs it and turns
    /// unexpected failures into a structured error the model can react to.
    /// </summary>
    private async ValueTask<object?> InvokeToolAsync(
        AgentTurnContext turn, FunctionInvocationContext context, CancellationToken ct)
    {
        var name = context.Function.Name;
        var argumentsJson = SerializeArguments(context.Arguments);
        var stopwatch = Stopwatch.StartNew();

        object? result;
        string? error;
        try
        {
            result = await context.Function.InvokeAsync(context.Arguments, ct);
            error = TryGetToolError(result);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or FormatException
                                   && !ct.IsCancellationRequested)
        {
            // Raised while binding arguments the model produced (wrong type, missing required value).
            logger.LogWarning(ex, "Tool {Tool} rejected arguments {Arguments}", name, argumentsJson);
            error = ToolErrorCodes.InvalidArguments;
            result = new ToolError(ToolErrorCodes.InvalidArguments, $"Arguments could not be read: {ex.Message}");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Tool {Tool} failed with arguments {Arguments}", name, argumentsJson);
            error = ToolErrorCodes.InternalError;
            result = new ToolError(ToolErrorCodes.InternalError,
                "The tool failed. Apologize briefly and suggest trying again later.");
        }

        stopwatch.Stop();
        turn.ToolCalls.Add(new ToolCallRecord(name, argumentsJson, error is null, stopwatch.ElapsedMilliseconds, error));

        logger.LogInformation(
            "Tool {Tool}({Arguments}) -> {Outcome} in {DurationMs} ms",
            name, argumentsJson, error ?? "ok", stopwatch.ElapsedMilliseconds);

        return result;
    }

    private static string SerializeArguments(AIFunctionArguments arguments) =>
        JsonSerializer.Serialize(
            arguments.ToDictionary(a => a.Key, a => a.Value),
            AIJsonUtilities.DefaultOptions);

    /// <summary>Returns the error code if a tool result is a <see cref="ToolError"/> (or its JSON form).</summary>
    internal static string? TryGetToolError(object? result) => result switch
    {
        ToolError e => e.Error,
        JsonElement { ValueKind: JsonValueKind.Object } json
            when json.TryGetProperty("error", out var code) && code.ValueKind == JsonValueKind.String
            => code.GetString(),
        _ => null
    };

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
