using System.ClientModel;
using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace BookingSystem.Ai.Knowledge;

/// <summary>Embeddings through the provider's OpenAI-compatible endpoint (Gemini by default).</summary>
public sealed class EmbeddingGeneratorFactory(IOptions<AiOptions> options) : IEmbeddingGeneratorFactory
{
    private IEmbeddingGenerator<string, Embedding<float>>? _generator;

    public string ModelId => $"{options.Value.Embedding.Provider}:{options.Value.Embedding.Model}";

    public IEmbeddingGenerator<string, Embedding<float>> Create()
    {
        if (_generator is not null) return _generator;

        var embedding = options.Value.Embedding;
        var provider = ChatClientFactory.ResolveProvider(options.Value, embedding.Provider);

        var client = new OpenAIClient(
            new ApiKeyCredential(provider.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(provider.Endpoint),
                NetworkTimeout = TimeSpan.FromSeconds(provider.TimeoutSeconds)
            });

        return _generator = client.GetEmbeddingClient(embedding.Model).AsIEmbeddingGenerator();
    }
}

/// <summary>
/// Query embeddings cache: experiments run the same queries in several modes and repeats,
/// so each distinct query is embedded once per process.
/// </summary>
public sealed class QueryEmbeddingCache
{
    private const int MaxEntries = 5000;
    private readonly ConcurrentDictionary<string, float[]> _cache = new(StringComparer.Ordinal);

    public async Task<float[]> GetOrAddAsync(string modelId, string query, Func<Task<float[]>> factory)
    {
        var key = modelId + "\u001f" + query.Trim();
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var vector = await factory();
        Set(modelId, query, vector);
        return vector;
    }

    public bool TryGet(string modelId, string text, out float[] vector) =>
        _cache.TryGetValue(modelId + "\u001f" + text.Trim(), out vector!);

    public void Set(string modelId, string text, float[] vector)
    {
        if (_cache.Count >= MaxEntries) _cache.Clear();
        _cache[modelId + "\u001f" + text.Trim()] = vector;
    }
}
