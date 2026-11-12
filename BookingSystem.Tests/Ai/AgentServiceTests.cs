using BookingSystem.Ai;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BookingSystem.Tests.Ai;

public class AgentServiceTests
{
    // 2026-11-20 08:00 UTC = 10:00 in Kyiv (Friday)
    private static readonly DateTimeOffset UtcNow = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<IChatClientFactory> _factory = new();
    private readonly AiOptions _options = new() { DefaultModel = "groq:test-model" };

    private AgentService CreateSut()
    {
        var options = Options.Create(_options);
        return new AgentService(
            _factory.Object,
            new AgentClock(new FixedTimeProvider(UtcNow), options),
            options,
            NullLoggerFactory.Instance,
            NullLogger<AgentService>.Instance);
    }

    [Fact]
    public async Task ChatAsync_ReturnsModelTextAndUsage()
    {
        var client = FakeChatClient.Replying("Вітаю! Чим допомогти?", inputTokens: 120, outputTokens: 12);
        _factory.Setup(f => f.Create("groq:test-model")).Returns(client);

        var reply = await CreateSut().ChatAsync(Guid.NewGuid(), null, "Привіт");

        reply.Text.Should().Be("Вітаю! Чим допомогти?");
        reply.IsDegraded.Should().BeFalse();
        reply.Model.Should().Be("groq:test-model");
        reply.InputTokens.Should().Be(120);
        reply.OutputTokens.Should().Be(12);
    }

    [Fact]
    public async Task ChatAsync_SendsUserMessageAndInstructionsWithLocalDate()
    {
        var client = FakeChatClient.Replying("ok");
        _factory.Setup(f => f.Create(It.IsAny<string>())).Returns(client);

        await CreateSut().ChatAsync(Guid.NewGuid(), "user-1", "Потрібне авто");

        client.Calls.Should().ContainSingle();
        var (messages, chatOptions) = client.Calls[0];

        messages.Should().Contain(m => m.Role == ChatRole.User && m.Text == "Потрібне авто");

        var instructions = string.Join("\n",
            new[] { chatOptions?.Instructions }
                .Concat(messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text)));
        instructions.Should().Contain("2026-11-20 10:00").And.Contain("Friday");
        instructions.Should().Contain("2026-11-20 Friday (today)")
            .And.Contain("2026-11-21 Saturday (tomorrow)")
            .And.Contain("2026-11-27 Friday");
        instructions.Should().Contain("signed in.");
        chatOptions!.Temperature.Should().Be(_options.Temperature);
    }

    [Fact]
    public async Task ChatAsync_UsesModelOverride()
    {
        _factory.Setup(f => f.Create("gemini:other")).Returns(FakeChatClient.Replying("ok"));

        var reply = await CreateSut().ChatAsync(Guid.NewGuid(), null, "hi", new AgentChatOptions { Model = "gemini:other" });

        reply.Model.Should().Be("gemini:other");
        _factory.Verify(f => f.Create("gemini:other"), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_WhenProviderNotConfigured_ReturnsDegradedReply()
    {
        _factory.Setup(f => f.Create(It.IsAny<string>())).Throws(new AiNotConfiguredException("no key"));

        var reply = await CreateSut().ChatAsync(Guid.NewGuid(), null, "hi");

        reply.IsDegraded.Should().BeTrue();
        reply.Text.Should().Be(AgentService.NotConfiguredText);
    }

    [Fact]
    public async Task ChatAsync_WhenProviderFails_ReturnsDegradedReply()
    {
        var client = new FakeChatClient((_, _) => throw new HttpRequestException("connection refused"));
        _factory.Setup(f => f.Create(It.IsAny<string>())).Returns(client);

        var reply = await CreateSut().ChatAsync(Guid.NewGuid(), null, "hi");

        reply.IsDegraded.Should().BeTrue();
        reply.Text.Should().Be(AgentService.UnavailableText);
    }
}
