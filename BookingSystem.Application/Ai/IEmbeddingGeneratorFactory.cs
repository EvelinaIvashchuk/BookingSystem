using Microsoft.Extensions.AI;

namespace BookingSystem.Ai;

/// <summary>Creates the embedding generator configured in <c>Ai:Embedding</c>.</summary>
public interface IEmbeddingGeneratorFactory
{
    /// <summary>Embedding model in <c>provider:model</c> form, stored with each indexed chunk.</summary>
    string ModelId { get; }

    /// <exception cref="AiNotConfiguredException">Provider is unknown or has no API key.</exception>
    IEmbeddingGenerator<string, Embedding<float>> Create();
}
