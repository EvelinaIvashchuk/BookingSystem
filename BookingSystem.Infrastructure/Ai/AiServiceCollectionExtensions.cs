using BookingSystem.Ai.Knowledge;
using BookingSystem.Ai.Slots;
using BookingSystem.Ai.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookingSystem.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>Registers the LLM agent, model factory and their configuration.</summary>
    public static IServiceCollection AddAiAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .PostConfigure(options => ApplyLegacyGroqKey(options, configuration))
            .Validate(o => o.MaxAgentIterations is >= 1 and <= 20, "Ai:MaxAgentIterations must be 1-20.")
            .Validate(o => o.HistoryLength >= 0, "Ai:HistoryLength must be >= 0.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AgentClock>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddScoped<BookingToolsFactory>();
        services.AddScoped<ISlotValidator, SlotValidator>();
        services.AddScoped<SlotFillingEngine>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<IPendingActionService, PendingActionService>();

        // Knowledge base and retrieval
        services.AddSingleton<IEmbeddingGeneratorFactory, EmbeddingGeneratorFactory>();
        services.AddSingleton<QueryEmbeddingCache>();
        services.AddSingleton<KnowledgeStore>();
        services.AddScoped<IFullTextIndex, MySqlFullTextIndex>();
        services.AddScoped<IKnowledgeSearch, KnowledgeSearch>();
        services.AddScoped<KnowledgeIndexer>();
        services.AddSingleton<Ranking.CarRanker>();
        services.AddHostedService<KnowledgeIndexingHostedService>();

        return services;
    }

    /// <summary>
    /// Backward compatibility: the key used to live in <c>Groq:ApiKey</c>
    /// (e.g. the <c>Groq__ApiKey</c> variable on Railway).
    /// </summary>
    private static void ApplyLegacyGroqKey(AiOptions options, IConfiguration configuration)
    {
        var legacyKey = configuration["Groq:ApiKey"];
        if (string.IsNullOrWhiteSpace(legacyKey)) return;

        if (!options.Providers.TryGetValue("groq", out var groq))
        {
            groq = new AiProviderOptions { Endpoint = "https://api.groq.com/openai/v1" };
            options.Providers["groq"] = groq;
        }

        if (string.IsNullOrWhiteSpace(groq.ApiKey))
            groq.ApiKey = legacyKey;
    }
}
