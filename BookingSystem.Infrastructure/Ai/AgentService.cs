using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using BookingSystem.Ai.Dtos;
using BookingSystem.Ai.Slots;
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
/// iteration limit and slot filling (<see cref="SlotFillingEngine"/>) wrap every tool call.
/// Conversation history lives in the database and the last <see cref="AiOptions.HistoryLength"/>
/// messages are replayed to the model on every turn.
/// </summary>
public sealed class AgentService(
    IChatClientFactory chatClientFactory,
    IUnitOfWork uow,
    BookingToolsFactory toolsFactory,
    SlotFillingEngine slotFilling,
    ISlotValidator slotValidator,
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
            var slotFillingEnabled = chatOptions?.SlotFillingEnabled ?? _options.SlotFilling.Enabled;
            var turn = new AgentTurnContext(
                session.Id, userId, clock.Today, message,
                BookingSlots.FromJson(session.SlotsJson), slotFillingEnabled);
            var agent = CreateAgent(model, turn);

            List<AiChatMessage> messages = [.. history.Select(ToAiMessage), new(ChatRole.User, message)];

            var response = await RunWithRetryAsync(agent, messages, model, session.Id, cancellationToken);

            stopwatch.Stop();

            var text = ReplyFormatter.ToPlainText(response.Text);
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
                Draft = turn.Draft,
                MissingSlots = turn.MissingSlots,
                AskedDates = DateClarification.DatesToAsk(turn, text),
                ToolCalls = [.. turn.ToolCalls],
                Model = model,
                InputTokens = ToInt(response.Usage?.InputTokenCount),
                OutputTokens = ToInt(BilledOutputTokens(response.Usage)),
                LatencyMs = stopwatch.ElapsedMilliseconds
            };

            if (turn.SlotFillingEnabled)
                session.SlotsJson = turn.Slots.ToJson();

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
            return Degraded(NotConfiguredText, session.Id, model, stopwatch, ex.Message);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            logger.LogError(ex, "AI provider call failed for session {SessionId}, model {Model}", session.Id, model);
            return Degraded(UnavailableText, session.Id, model, stopwatch, ex.Message);
        }
    }

    internal const int HistoryMessagesShown = 50;

    public async Task<ChatHistory?> GetHistoryAsync(Guid sessionId, string? userId, CancellationToken cancellationToken = default)
    {
        var session = sessionId == Guid.Empty ? null : await uow.ChatSessions.GetAsync(sessionId, cancellationToken);

        // Same ownership rule as ChatAsync: a signed-in user's session is visible only to them.
        if (session is null || (session.UserId is not null && session.UserId != userId))
            return null;

        var messages = (await uow.ChatSessions.GetRecentMessagesAsync(session.Id, HistoryMessagesShown, cancellationToken))
            .Where(m => !m.IsInternalNote)
            .Select(m => new ChatHistoryMessage(
                m.Role,
                m.Role == ChatSessionMessage.AssistantRole ? ReplyFormatter.ToPlainText(m.Content) : m.Content,
                m.CreatedAt))
            .ToList();

        RentalDraft? draft = null;
        if (userId is not null)
        {
            var pending = (await uow.PendingActions.GetPendingBySessionAsync(session.Id, cancellationToken))
                .Where(a => a.UserId == userId && a.ExpiresAt > clock.UtcNow)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault();
            if (pending is not null)
                draft = JsonSerializer.Deserialize<RentalDraft>(pending.PayloadJson, PendingActionService.PayloadJsonOptions);
        }

        return new ChatHistory(session.Id, messages, draft);
    }

    /// <summary>
    /// Runs the agent; retries once when the provider rejected a malformed tool call the model
    /// generated (Groq: HTTP 400 <c>tool_use_failed</c>) — a model glitch, not an outage.
    /// </summary>
    private async Task<AgentResponse> RunWithRetryAsync(
        ChatClientAgent agent, List<AiChatMessage> messages, string model, Guid sessionId, CancellationToken ct)
    {
        try
        {
            return await agent.RunAsync(messages, session: null, options: null, ct);
        }
        catch (ClientResultException ex) when (IsRejectedToolCall(ex))
        {
            logger.LogWarning(
                "Provider rejected a tool call generated by {Model} for session {SessionId}; retrying once. {Error}",
                model, sessionId, ex.Message.Split('\n').FirstOrDefault(l => l.Contains("validation")) ?? ex.Message);
            return await agent.RunAsync(messages, session: null, options: null, ct);
        }
    }

    internal static bool IsRejectedToolCall(ClientResultException ex) =>
        ex.Status == 400 && ex.Message.Contains("tool_use_failed", StringComparison.Ordinal);

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
                    Instructions = AgentPrompts.BuildInstructions(
                        clock.Now,
                        isAuthenticated: turn.UserId is not null,
                        knownSlots: turn.SlotFillingEnabled ? turn.Slots : null),
                    Temperature = _options.Temperature,
                    MaxOutputTokens = _options.MaxOutputTokens,
                    Tools = tools
                }
            },
            loggerFactory);
    }

    /// <summary>
    /// Single entry point for every tool call the model makes. With slot filling on, the call is
    /// merged with known slots and validated first, and blocked if parameters are missing or invalid.
    /// Every call is measured and logged; unexpected failures become a structured error.
    /// </summary>
    private async ValueTask<object?> InvokeToolAsync(
        AgentTurnContext turn, FunctionInvocationContext context, CancellationToken ct)
    {
        var name = context.Function.Name;
        var argumentsJson = SerializeArguments(context.Arguments);
        var stopwatch = Stopwatch.StartNew();

        // Verdict on the model's own arguments, recorded in both modes (shadow when slot filling is off).
        var proposed = await ValidateProposedAsync(name, context.Arguments, turn, ct);

        SlotFillingDecision? decision = null;
        if (turn.SlotFillingEnabled)
        {
            decision = await slotFilling.PrepareAsync(name, context.Arguments, turn, ct);

            if (!decision.CanExecute)
            {
                var blocked = SlotFillingEngine.ToToolError(decision.Validation!);
                turn.MissingSlots = decision.Validation!.Missing;
                stopwatch.Stop();

                turn.ToolCalls.Add(new ToolCallRecord(name, argumentsJson, false, stopwatch.ElapsedMilliseconds, blocked.Error)
                {
                    Intercepted = true,
                    MergedSlots = decision.MergedSlots,
                    ProposedMissing = proposed?.Missing,
                    ProposedInvalid = proposed?.Invalid
                });

                logger.LogInformation(
                    "Tool {Tool}({Arguments}) blocked by slot filling: missing [{Missing}], invalid [{Invalid}]",
                    name, argumentsJson, string.Join(", ", blocked.Missing ?? []), string.Join(" | ", blocked.Invalid ?? []));

                return blocked;
            }
        }

        var effectiveJson = decision?.Validation is null ? null : SerializeArguments(context.Arguments);

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

        if (error is null && decision is not null)
        {
            SlotFillingEngine.Commit(name, decision, turn.Slots);
            if (decision.Validation is not null) turn.MissingSlots = [];
        }

        stopwatch.Stop();
        turn.ToolCalls.Add(new ToolCallRecord(name, argumentsJson, error is null, stopwatch.ElapsedMilliseconds, error)
        {
            EffectiveArgumentsJson = effectiveJson,
            MergedSlots = decision?.MergedSlots,
            ProposedMissing = proposed?.Missing,
            ProposedInvalid = proposed?.Invalid
        });

        logger.LogInformation(
            "Tool {Tool}({Arguments}) -> {Outcome} in {DurationMs} ms",
            name, effectiveJson ?? argumentsJson, error ?? "ok", stopwatch.ElapsedMilliseconds);

        return result;
    }

    /// <summary>Validates the arguments exactly as the model sent them, without changing anything.</summary>
    private async Task<SlotValidationResult?> ValidateProposedAsync(
        string toolName, AIFunctionArguments arguments, AgentTurnContext turn, CancellationToken ct)
    {
        if (SlotSchema.For(toolName) is null) return null;

        var snapshot = new Dictionary<string, object?>(arguments);
        var provided = snapshot.Where(a => !SlotValues.IsEmpty(a.Value)).Select(a => a.Key).ToHashSet();

        return await slotValidator.ValidateAsync(
            toolName, snapshot, new SlotValidationContext(turn.Today, turn.LatestUserMessage, provided), ct);
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

    private static AgentReply Degraded(string text, Guid sessionId, string model, Stopwatch stopwatch, string? error = null) => new()
    {
        ErrorDetail = error,
        Text = text,
        SessionId = sessionId,
        Model = model,
        IsDegraded = true,
        LatencyMs = stopwatch.ElapsedMilliseconds
    };

    private static int? ToInt(long? value) => value is null ? null : (int)Math.Min(value.Value, int.MaxValue);

    /// <summary>
    /// Output tokens that are billed, including reasoning ("thinking") tokens. Gemini's OpenAI-compatible API
    /// leaves thinking tokens out of <c>completion_tokens</c> and reports them only in <c>total_tokens</c>,
    /// so the larger of the output count and total − input is used (for other providers both are equal).
    /// </summary>
    internal static long? BilledOutputTokens(UsageDetails? usage)
    {
        if (usage is null) return null;
        var output = usage.OutputTokenCount;
        if (usage is { TotalTokenCount: { } total, InputTokenCount: { } input } && total - input > (output ?? 0))
            output = total - input;
        return output;
    }
}
