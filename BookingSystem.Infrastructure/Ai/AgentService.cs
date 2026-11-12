using System.ClientModel;
using System.Diagnostics;
using BookingSystem.Ai.Dtos;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace BookingSystem.Ai;

/// <summary>
/// Runs one chat turn through a <see cref="ChatClientAgent"/> built on top of the configured model.
/// The function-invocation loop is assembled here (not by the agent's defaults) so that the
/// iteration limit and, later, slot validation are under our control.
/// </summary>
public sealed class AgentService(
    IChatClientFactory chatClientFactory,
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
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var agent = CreateAgent(model, isAuthenticated: userId is not null, tools: []);

            List<ChatMessage> messages = [new(ChatRole.User, message)];

            var response = await agent.RunAsync(messages, session: null, options: null, cancellationToken);

            stopwatch.Stop();

            logger.LogInformation(
                "AI turn completed: session {SessionId}, model {Model}, {InputTokens} in / {OutputTokens} out tokens, {LatencyMs} ms",
                sessionId, model, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, stopwatch.ElapsedMilliseconds);

            return new AgentReply
            {
                Text = response.Text.Trim(),
                Model = model,
                InputTokens = ToInt(response.Usage?.InputTokenCount),
                OutputTokens = ToInt(response.Usage?.OutputTokenCount),
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (AiNotConfiguredException ex)
        {
            logger.LogWarning("AI turn skipped: {Reason}", ex.Message);
            return Degraded(NotConfiguredText, model, stopwatch);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            logger.LogError(ex, "AI provider call failed for session {SessionId}, model {Model}", sessionId, model);
            return Degraded(UnavailableText, model, stopwatch);
        }
    }

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

    private static AgentReply Degraded(string text, string model, Stopwatch stopwatch) => new()
    {
        Text = text,
        Model = model,
        IsDegraded = true,
        LatencyMs = stopwatch.ElapsedMilliseconds
    };

    private static int? ToInt(long? value) => value is null ? null : (int)Math.Min(value.Value, int.MaxValue);
}
