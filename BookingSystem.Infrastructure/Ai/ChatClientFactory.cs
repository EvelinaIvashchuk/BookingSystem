using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace BookingSystem.Ai;

/// <summary>
/// Builds <see cref="IChatClient"/> instances for any OpenAI-compatible provider
/// (Groq, Gemini) by pointing the OpenAI SDK at the provider's endpoint.
/// Clients are cached per model id; they are thread-safe and hold no conversation state.
/// </summary>
public sealed class ChatClientFactory(IOptions<AiOptions> options) : IChatClientFactory
{
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public IChatClient Create(string? modelId = null)
    {
        var id = string.IsNullOrWhiteSpace(modelId) ? options.Value.DefaultModel : modelId.Trim();
        return _clients.GetOrAdd(id, Build);
    }

    private IChatClient Build(string id)
    {
        var model = ModelRef.Parse(id);
        var provider = ResolveProvider(options.Value, model.Provider);

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(provider.Endpoint),
            NetworkTimeout = TimeSpan.FromSeconds(provider.TimeoutSeconds)
        };

        if (IsGemini(clientOptions.Endpoint))
        {
            // Gemini 3 needs thought signatures echoed back with tool calls (see GeminiThoughtSignatureHandler).
            var handler = new GeminiThoughtSignatureHandler(
                new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
            clientOptions.Transport = new HttpClientPipelineTransport(
                new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds + 30) });
        }

        var client = new OpenAIClient(new ApiKeyCredential(provider.ApiKey), clientOptions);
        return client.GetChatClient(model.Model).AsIChatClient();
    }

    internal static bool IsGemini(Uri endpoint) =>
        endpoint.Host.Equals("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);

    internal static AiProviderOptions ResolveProvider(AiOptions options, string providerName)
    {
        if (!options.Providers.TryGetValue(providerName, out var provider)
            || string.IsNullOrWhiteSpace(provider.Endpoint))
            throw new AiNotConfiguredException($"AI provider '{providerName}' is not configured.");

        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            throw new AiNotConfiguredException($"AI provider '{providerName}' has no API key.");

        return provider;
    }
}
