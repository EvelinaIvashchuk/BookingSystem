using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace BookingSystem.Ai;

/// <summary>
/// HTTP handler for Gemini's OpenAI-compatible endpoint.
/// <para>
/// Gemini 3 models return a <c>thought_signature</c> in <c>tool_calls[i].extra_content.google</c> and
/// reject the next request (HTTP 400 "Function call is missing a thought_signature") unless it is sent back
/// with the same function call. The OpenAI SDK drops unknown fields, so this handler remembers the
/// signature per tool call id from responses and puts it back into the assistant messages of requests.
/// A call whose signature is unknown (e.g. after a restart) gets Google's documented placeholder.
/// </para>
/// <para>
/// Gemini also returns errors as a JSON array (<c>[{"error":{...}}]</c>), which the OpenAI SDK cannot parse,
/// so the error text (e.g. a daily quota) would be lost; the array is unwrapped to the single error object.
/// </para>
/// Conversation history is replayed as text only, so signatures are needed only within one agent turn
/// (the function-invocation loop) and are kept for <see cref="Retention"/>.
/// </summary>
public sealed class GeminiThoughtSignatureHandler : DelegatingHandler
{
    /// <summary>Placeholder accepted by Gemini for function calls without a known signature.</summary>
    internal const string PlaceholderSignature = "skip_thought_signature_validator";

    internal static readonly TimeSpan Retention = TimeSpan.FromHours(1);
    private const int PruneThreshold = 2000;

    private readonly ConcurrentDictionary<string, (JsonNode ExtraContent, DateTime StoredAt)> _signatures = new();
    private readonly TimeProvider _time;

    public GeminiThoughtSignatureHandler(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public GeminiThoughtSignatureHandler(HttpMessageHandler innerHandler, TimeProvider? time = null)
        : base(innerHandler) => _time = time ?? TimeProvider.System;

    internal int StoredSignatures => _signatures.Count;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            if (AddSignatures(body) is { } patched)
                request.Content = Json(patched, request.Content);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Content is null)
            return response;

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
            RememberSignatures(text);
        else if (UnwrapErrorArray(text) is { } error)
            response.Content = Json(error, response.Content);
        // otherwise the content is buffered by ReadAsStringAsync and stays readable for the SDK

        return response;
    }

    /// <summary>Stores <c>extra_content</c> of every tool call in a chat completion response.</summary>
    internal void RememberSignatures(string responseJson)
    {
        if (!responseJson.Contains("thought_signature", StringComparison.Ordinal))
            return;

        JsonNode? root;
        try { root = JsonNode.Parse(responseJson); }
        catch (System.Text.Json.JsonException) { return; }

        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var choice in root?["choices"]?.AsArray() ?? [])
        foreach (var call in choice?["message"]?["tool_calls"]?.AsArray() ?? [])
        {
            if (call?["id"]?.GetValue<string>() is { Length: > 0 } id && call["extra_content"] is { } extra)
                _signatures[id] = (extra.DeepClone(), now);
        }

        if (_signatures.Count > PruneThreshold)
            Prune(now);
    }

    /// <summary>
    /// Returns the request body with signatures added to assistant tool calls, or null when nothing changed.
    /// In each assistant message the first tool call must carry a signature (parallel calls carry one on the first only).
    /// </summary>
    internal string? AddSignatures(string requestJson)
    {
        if (!requestJson.Contains("\"tool_calls\"", StringComparison.Ordinal))
            return null;

        JsonNode? root;
        try { root = JsonNode.Parse(requestJson); }
        catch (System.Text.Json.JsonException) { return null; }

        var changed = false;
        foreach (var message in root?["messages"]?.AsArray() ?? [])
        {
            if (message?["role"]?.GetValue<string>() != "assistant" || message["tool_calls"] is not JsonArray calls || calls.Count == 0)
                continue;

            foreach (var call in calls)
            {
                if (call is null || call["extra_content"] is not null) continue;
                if (call["id"]?.GetValue<string>() is { } id && _signatures.TryGetValue(id, out var stored))
                {
                    call["extra_content"] = stored.ExtraContent.DeepClone();
                    changed = true;
                }
            }

            if (calls[0] is JsonObject first && first["extra_content"] is null)
            {
                first["extra_content"] = new JsonObject
                {
                    ["google"] = new JsonObject { ["thought_signature"] = PlaceholderSignature }
                };
                changed = true;
            }
        }

        return changed ? root!.ToJsonString() : null;
    }

    /// <summary><c>[{"error":{...}}]</c> → <c>{"error":{...}}</c>; null for any other body.</summary>
    internal static string? UnwrapErrorArray(string body)
    {
        if (!body.TrimStart().StartsWith('['))
            return null;
        try
        {
            return JsonNode.Parse(body) is JsonArray { Count: > 0 } array && array[0] is JsonObject error && error["error"] is not null
                ? error.ToJsonString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private void Prune(DateTime now)
    {
        foreach (var (id, entry) in _signatures)
            if (now - entry.StoredAt > Retention)
                _signatures.TryRemove(id, out _);
    }

    private static StringContent Json(string json, HttpContent original)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        foreach (var header in original.Headers)
        {
            if (header.Key is "Content-Type" or "Content-Length" or "Content-Encoding") continue;
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return content;
    }
}
