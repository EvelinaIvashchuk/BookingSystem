namespace BookingSystem.Ai;

/// <summary>A chat model an administrator may choose, with its price from <see cref="AiOptions.Pricing"/> (0 if unknown).</summary>
/// <param name="Id">Model in <c>provider:model</c> form.</param>
public sealed record ChatModelOption(string Id, string Provider, string Model, decimal InputPer1M, decimal OutputPer1M);

/// <summary>
/// The chat model used by the agent, selectable by an administrator at runtime. The choice is stored in the database,
/// so it survives restarts and applies from the next message without redeploying. Until an administrator chooses,
/// <see cref="AiOptions.DefaultModel"/> is used.
/// </summary>
public interface IChatModelSettings
{
    /// <summary>
    /// Models that can be chosen: <see cref="AiOptions.DefaultModel"/> plus every model listed in <see cref="AiOptions.Pricing"/>,
    /// limited to providers that are configured with an API key. The default model comes first.
    /// </summary>
    IReadOnlyList<ChatModelOption> GetAvailableModels();

    /// <summary>
    /// The model for the next chat turn: the administrator's choice, or <see cref="AiOptions.DefaultModel"/> when nothing was
    /// chosen or the chosen model is no longer available (e.g. its provider lost the key).
    /// </summary>
    Task<string> GetActiveModelAsync(CancellationToken cancellationToken = default);

    /// <exception cref="ArgumentException"><paramref name="modelId"/> is not in <see cref="GetAvailableModels"/>.</exception>
    Task SetActiveModelAsync(string modelId, CancellationToken cancellationToken = default);
}
