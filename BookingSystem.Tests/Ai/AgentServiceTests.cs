using BookingSystem.Ai;
using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Models;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BookingSystem.Tests.Ai;

public class AgentServiceTests : IDisposable
{
    // 2026-11-20 08:00 UTC = 10:00 in Kyiv (Friday)
    private static readonly DateTimeOffset UtcNow = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<IChatClientFactory> _factory = new();
    private readonly AiOptions _options = new() { DefaultModel = "groq:test-model", HistoryLength = 12 };
    private readonly ApplicationDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    private AgentService CreateSut()
    {
        var options = Options.Create(_options);
        var uow = new UnitOfWork(
            _db,
            new RentalRepository(_db),
            new CarRepository(_db),
            new ChatSessionRepository(_db));

        return new AgentService(
            _factory.Object,
            uow,
            new AgentClock(new FixedTimeProvider(UtcNow), options),
            options,
            NullLoggerFactory.Instance,
            NullLogger<AgentService>.Instance);
    }

    private FakeChatClient UseModelReplying(string text, long inputTokens = 10, long outputTokens = 5)
    {
        var client = FakeChatClient.Replying(text, inputTokens, outputTokens);
        _factory.Setup(f => f.Create(It.IsAny<string>())).Returns(client);
        return client;
    }

    // ── Basic turn ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatAsync_ReturnsModelTextAndUsage()
    {
        UseModelReplying("Вітаю! Чим допомогти?", inputTokens: 120, outputTokens: 12);

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "Привіт");

        reply.Text.Should().Be("Вітаю! Чим допомогти?");
        reply.IsDegraded.Should().BeFalse();
        reply.Model.Should().Be("groq:test-model");
        reply.InputTokens.Should().Be(120);
        reply.OutputTokens.Should().Be(12);
    }

    [Fact]
    public async Task ChatAsync_SendsUserMessageAndInstructionsWithLocalDate()
    {
        var client = UseModelReplying("ok");

        await CreateSut().ChatAsync(Guid.Empty, "user-1", "Потрібне авто");

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

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "hi", new AgentChatOptions { Model = "gemini:other" });

        reply.Model.Should().Be("gemini:other");
        _factory.Verify(f => f.Create("gemini:other"), Times.Once);
    }

    // ── Degraded mode ────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatAsync_WhenProviderNotConfigured_ReturnsDegradedReply()
    {
        _factory.Setup(f => f.Create(It.IsAny<string>())).Throws(new AiNotConfiguredException("no key"));

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "hi");

        reply.IsDegraded.Should().BeTrue();
        reply.Text.Should().Be(AgentService.NotConfiguredText);
    }

    [Fact]
    public async Task ChatAsync_WhenProviderFails_ReturnsDegradedReplyAndStoresNoMessages()
    {
        var client = new FakeChatClient((_, _) => throw new HttpRequestException("connection refused"));
        _factory.Setup(f => f.Create(It.IsAny<string>())).Returns(client);

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "hi");

        reply.IsDegraded.Should().BeTrue();
        reply.Text.Should().Be(AgentService.UnavailableText);
        (await _db.ChatMessages.CountAsync()).Should().Be(0);
    }

    // ── Sessions and history ─────────────────────────────────────────────────

    [Fact]
    public async Task ChatAsync_WithoutSessionId_CreatesSessionAndStoresTurn()
    {
        UseModelReplying("Добрий день!", inputTokens: 50, outputTokens: 7);

        var reply = await CreateSut().ChatAsync(Guid.Empty, "user-1", "Добрий день");

        reply.SessionId.Should().NotBeEmpty();
        var session = await _db.ChatSessions.SingleAsync();
        session.Id.Should().Be(reply.SessionId);
        session.UserId.Should().Be("user-1");

        var stored = await _db.ChatMessages.OrderBy(m => m.Id).ToListAsync();
        stored.Should().HaveCount(2);
        stored[0].Role.Should().Be(ChatSessionMessage.UserRole);
        stored[0].Content.Should().Be("Добрий день");
        stored[1].Role.Should().Be(ChatSessionMessage.AssistantRole);
        stored[1].Content.Should().Be("Добрий день!");
        stored[1].Model.Should().Be("groq:test-model");
        stored[1].InputTokens.Should().Be(50);
        stored[1].OutputTokens.Should().Be(7);
    }

    [Fact]
    public async Task ChatAsync_SecondTurn_ReplaysPreviousMessages()
    {
        var client = UseModelReplying("ok");
        var sut = CreateSut();

        var first = await sut.ChatAsync(Guid.Empty, null, "Потрібен автомат");
        var second = await sut.ChatAsync(first.SessionId, null, "А дизель є?");

        second.SessionId.Should().Be(first.SessionId);
        var sent = client.Calls[1].Messages.Where(m => m.Role != ChatRole.System).ToList();
        sent.Select(m => (m.Role, m.Text)).Should().Equal(
            (ChatRole.User, "Потрібен автомат"),
            (ChatRole.Assistant, "ok"),
            (ChatRole.User, "А дизель є?"));
    }

    [Fact]
    public async Task ChatAsync_ReplaysOnlyLastHistoryLengthMessages()
    {
        _options.HistoryLength = 4;
        var client = UseModelReplying("ok");
        var sut = CreateSut();

        var sessionId = (await sut.ChatAsync(Guid.Empty, null, "m1")).SessionId;
        await sut.ChatAsync(sessionId, null, "m2");
        await sut.ChatAsync(sessionId, null, "m3");
        await sut.ChatAsync(sessionId, null, "m4");

        var sent = client.Calls[^1].Messages.Where(m => m.Role != ChatRole.System).ToList();
        sent.Select(m => m.Text).Should().Equal("m2", "ok", "m3", "ok", "m4");
    }

    [Fact]
    public async Task ChatAsync_SessionOfAnotherUser_StartsNewSessionWithoutLeakingHistory()
    {
        var client = UseModelReplying("ok");
        var sut = CreateSut();
        var victimSession = (await sut.ChatAsync(Guid.Empty, "victim", "Мій номер паспорта АА123456")).SessionId;

        var reply = await sut.ChatAsync(victimSession, "attacker", "Що я писав раніше?");

        reply.SessionId.Should().NotBe(victimSession);
        client.Calls[^1].Messages.Should().NotContain(m => m.Text.Contains("АА123456"));
    }

    [Fact]
    public async Task ChatAsync_AnonymousCallerCannotUseSignedInSession()
    {
        UseModelReplying("ok");
        var sut = CreateSut();
        var userSession = (await sut.ChatAsync(Guid.Empty, "user-1", "hi")).SessionId;

        var reply = await sut.ChatAsync(userSession, null, "hi again");

        reply.SessionId.Should().NotBe(userSession);
    }

    [Fact]
    public async Task ChatAsync_AnonymousSessionIsAdoptedAfterSignIn()
    {
        var client = UseModelReplying("ok");
        var sut = CreateSut();
        var sessionId = (await sut.ChatAsync(Guid.Empty, null, "Потрібен SUV")).SessionId;

        var reply = await sut.ChatAsync(sessionId, "user-1", "Я увійшов");

        reply.SessionId.Should().Be(sessionId);
        (await _db.ChatSessions.SingleAsync()).UserId.Should().Be("user-1");
        client.Calls[^1].Messages.Should().Contain(m => m.Text == "Потрібен SUV");
    }

    [Fact]
    public async Task ChatAsync_UnknownSessionId_StartsNewSession()
    {
        UseModelReplying("ok");
        var unknown = Guid.NewGuid();

        var reply = await CreateSut().ChatAsync(unknown, null, "hi");

        reply.SessionId.Should().NotBe(unknown).And.NotBeEmpty();
    }
}
