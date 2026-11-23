using BookingSystem.Ai;
using BookingSystem.Ai.Knowledge;
using Microsoft.Extensions.AI;

namespace BookingSystem.Tests.Ai.Knowledge;

/// <summary>Embedding generator with a scripted text → vector mapping; counts calls.</summary>
internal sealed class FakeEmbeddingGenerator(Func<string, float[]> embed) : IEmbeddingGenerator<string, Embedding<float>>
{
    public int Calls { get; private set; }
    public int TextsEmbedded { get; private set; }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        var list = values.ToList();
        TextsEmbedded += list.Count;
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list.Select(v => new Embedding<float>(embed(v)))));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

internal sealed class FakeEmbeddingFactory(IEmbeddingGenerator<string, Embedding<float>>? generator, string modelId = "test:embed")
    : IEmbeddingGeneratorFactory
{
    public string ModelId { get; } = modelId;

    public IEmbeddingGenerator<string, Embedding<float>> Create() =>
        generator ?? throw new AiNotConfiguredException("no embedding key");
}

/// <summary>Full-text index returning a scripted ranking for each normalized query.</summary>
internal sealed class FakeFullTextIndex(Dictionary<string, int[]> rankings) : IFullTextIndex
{
    public List<string> Queries { get; } = [];

    public Task<IReadOnlyList<(int Id, double Score)>> SearchAsync(string searchText, int limit, CancellationToken ct = default)
    {
        Queries.Add(searchText);
        var ids = rankings.GetValueOrDefault(searchText) ?? [];
        IReadOnlyList<(int, double)> result = ids.Take(limit).Select((id, i) => (id, 10.0 - i)).ToList();
        return Task.FromResult(result);
    }

    /// <summary>Feedback documents passed to each expanded search.</summary>
    public List<IReadOnlyList<int>> Feedback { get; } = [];

    public Task<IReadOnlyList<(int Id, double Score)>> SearchExpandedAsync(
        string searchText, IReadOnlyList<int> feedbackIds, QueryExpansionOptions expansion, int limit, CancellationToken ct = default)
    {
        Feedback.Add(feedbackIds);
        return SearchAsync(searchText, limit, ct);
    }
}
