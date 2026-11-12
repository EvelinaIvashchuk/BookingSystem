using Microsoft.Extensions.AI;

namespace BookingSystem.Tests.Ai;

/// <summary>Scriptable <see cref="IChatClient"/> that records what the agent sends to the model.</summary>
internal sealed class FakeChatClient(Func<IList<ChatMessage>, ChatOptions?, ChatResponse> respond) : IChatClient
{
    public List<(List<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Calls.Add((list, options));
        return Task.FromResult(respond(list, options));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    public static FakeChatClient Replying(string text, long inputTokens = 10, long outputTokens = 5) =>
        new((_, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = outputTokens }
        });
}

/// <summary>Clock pinned to a fixed UTC instant.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
