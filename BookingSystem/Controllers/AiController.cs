using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BookingSystem.Controllers;

public class AiController(IHttpClientFactory httpClientFactory, IConfiguration configuration) : Controller
{
    private const string GroqApiUrl = "https://api.groq.com/openai/v1/chat/completions";
    private const string Model = "llama-3.3-70b-versatile";
    private const string SystemPrompt =
        "You are a helpful assistant for CarRental — a car rental service. " +
        "Answer briefly and friendly. Help customers with questions about renting a car, prices, required documents, " +
        "booking process, cancellation policy, and available car options. " +
        "Always respond in the same language the user writes in. " +
        "If you don't know the exact answer, suggest contacting support.";

    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Message))
            return BadRequest();

        var apiKey = configuration["Groq:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return StatusCode(503, new { reply = "AI service is not configured." });

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user",   content = request.Message }
            },
            max_tokens = 512,
            temperature = 0.7
        };

        var json = JsonSerializer.Serialize(payload);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(GroqApiUrl, httpContent);
        }
        catch
        {
            return StatusCode(503, new { reply = "AI service is unavailable. Please try again later." });
        }

        if (!response.IsSuccessStatusCode)
            return StatusCode(503, new { reply = "AI service returned an error. Please try again later." });

        var responseBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);
        var reply = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        return Ok(new { reply });
    }
}

public sealed class ChatRequest
{
    public string? Message { get; set; }
}
