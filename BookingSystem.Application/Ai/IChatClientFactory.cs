using Microsoft.Extensions.AI;

namespace BookingSystem.Ai;

/// <summary>
/// Creates chat clients for a model given as <c>provider:model</c>
/// (e.g. <c>gemini:gemini-3.8-flash</c>). Lets experiments switch models without code changes.
/// </summary>
public interface IChatClientFactory
{
    /// <param name="modelId">Model in <c>provider:model</c> form; <c>null</c> means <see cref="AiOptions.DefaultModel"/>.</param>
    /// <exception cref="AiNotConfiguredException">Provider is unknown or has no API key.</exception>
    IChatClient Create(string? modelId = null);
}

/// <summary>Thrown when the requested AI provider is missing from configuration or has no key.</summary>
public sealed class AiNotConfiguredException(string message) : Exception(message);
