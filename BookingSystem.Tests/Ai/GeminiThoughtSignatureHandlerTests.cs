using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BookingSystem.Ai;
using FluentAssertions;
using Microsoft.Extensions.AI;
using OpenAI;

namespace BookingSystem.Tests.Ai;

public class GeminiThoughtSignatureHandlerTests
{
    private const string Signature = "EpwBCpkBAWkUfRMf-real-signature";

    private const string ToolCallResponse = $$$"""
        {"choices":[{"finish_reason":"tool_calls","index":0,"message":{"role":"assistant","tool_calls":[
          {"extra_content":{"google":{"thought_signature":"{{{Signature}}}"}},
           "function":{"arguments":"{\"city\":\"Kyiv\"}","name":"get_weather"},"id":"call_1","type":"function"}]}}],
         "created":1,"id":"r1","model":"gemini-3.1-flash-lite","object":"chat.completion",
         "usage":{"completion_tokens":17,"prompt_tokens":46,"total_tokens":63}}
        """;

    private const string TextResponse = """
        {"choices":[{"finish_reason":"stop","index":0,"message":{"role":"assistant","content":"5°C, cloudy."}}],
         "created":2,"id":"r2","model":"gemini-3.1-flash-lite","object":"chat.completion",
         "usage":{"completion_tokens":9,"prompt_tokens":70,"total_tokens":79}}
        """;

    [Fact]
    public async Task FunctionLoop_SendsSignatureBackWithToolCall()
    {
        var server = new ScriptedHandler(ToolCallResponse, TextResponse);
        var chat = new ChatClientBuilder(GeminiClient(server))
            .UseFunctionInvocation()
            .Build();
        var weather = AIFunctionFactory.Create((string city) => "5C cloudy", "get_weather");

        var response = await chat.GetResponseAsync("Weather in Kyiv?", new ChatOptions { Tools = [weather] });

        response.Text.Should().Be("5°C, cloudy.");
        server.Requests.Should().HaveCount(2);
        var assistant = JsonNode.Parse(server.Requests[1])!["messages"]!.AsArray()
            .Single(m => m!["role"]!.GetValue<string>() == "assistant")!;
        assistant["tool_calls"]![0]!["extra_content"]!["google"]!["thought_signature"]!.GetValue<string>()
            .Should().Be(Signature);
    }

    [Fact]
    public void AddSignatures_UnknownCall_GetsPlaceholderOnFirstCallOnly()
    {
        var handler = new GeminiThoughtSignatureHandler();
        var request = """
            {"model":"m","messages":[{"role":"user","content":"hi"},
              {"role":"assistant","tool_calls":[
                {"id":"a","type":"function","function":{"name":"f","arguments":"{}"}},
                {"id":"b","type":"function","function":{"name":"g","arguments":"{}"}}]},
              {"role":"tool","tool_call_id":"a","content":"1"},{"role":"tool","tool_call_id":"b","content":"2"}]}
            """;

        var patched = JsonNode.Parse(handler.AddSignatures(request)!)!;

        var calls = patched["messages"]![1]!["tool_calls"]!.AsArray();
        calls[0]!["extra_content"]!["google"]!["thought_signature"]!.GetValue<string>()
            .Should().Be(GeminiThoughtSignatureHandler.PlaceholderSignature);
        calls[1]!["extra_content"].Should().BeNull();
    }

    [Fact]
    public void AddSignatures_NoToolCalls_LeavesRequestUntouched()
    {
        var handler = new GeminiThoughtSignatureHandler();

        handler.AddSignatures("""{"model":"m","messages":[{"role":"user","content":"hi"}]}""").Should().BeNull();
    }

    [Fact]
    public void RememberSignatures_StoresSignaturePerCallId()
    {
        var handler = new GeminiThoughtSignatureHandler();

        handler.RememberSignatures(ToolCallResponse);
        handler.RememberSignatures(TextResponse);

        handler.StoredSignatures.Should().Be(1);
    }

    [Fact]
    public async Task ErrorArray_IsUnwrapped_SoTheProviderMessageReachesTheCaller()
    {
        var error = """
            [{"error":{"code":429,"message":"Quota exceeded for metric: generate_content_requests_per_day","status":"RESOURCE_EXHAUSTED"}}]
            """;
        var server = new ScriptedHandler((HttpStatusCode.TooManyRequests, error));
        var chat = GeminiClient(server);

        var act = () => chat.GetResponseAsync("hi");

        (await act.Should().ThrowAsync<ClientResultException>())
            .Which.Message.Should().Contain("requests_per_day");
    }

    [Theory]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/openai/", true)]
    [InlineData("https://api.groq.com/openai/v1", false)]
    public void IsGemini_DetectsGoogleEndpoint(string endpoint, bool expected) =>
        ChatClientFactory.IsGemini(new Uri(endpoint)).Should().Be(expected);

    private static IChatClient GeminiClient(HttpMessageHandler server)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"),
            Transport = new HttpClientPipelineTransport(new HttpClient(new GeminiThoughtSignatureHandler(server))),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        };
        return new OpenAIClient(new ApiKeyCredential("test-key"), options)
            .GetChatClient("gemini-3.1-flash-lite").AsIChatClient();
    }

    /// <summary>Returns the scripted responses in order and records request bodies.</summary>
    private sealed class ScriptedHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _next;

        public ScriptedHandler(params string[] okBodies)
            : this(okBodies.Select(b => (HttpStatusCode.OK, b)).ToArray()) { }

        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            var (status, body) = responses[_next++];
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
