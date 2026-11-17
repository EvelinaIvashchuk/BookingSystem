using BookingSystem.Ai;
using BookingSystem.Ai.Dtos;
using BookingSystem.Ai.Slots;
using BookingSystem.Ai.Tools;
using BookingSystem.Data;
using BookingSystem.Models;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BookingSystem.Tests.Ai;

public class AgentServiceTests : IAsyncLifetime
{
    // 2026-11-20 08:00 UTC = 10:00 in Kyiv (Friday)
    private static readonly DateTimeOffset UtcNow = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<IChatClientFactory> _factory = new();
    private readonly AiOptions _options = new() { DefaultModel = "groq:test-model", HistoryLength = 12 };
    private readonly ApplicationDbContext _db = TestDbContextFactory.Create();

    public Task InitializeAsync() => AiTestData.SeedAsync(_db);

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private AgentService CreateSut()
    {
        var options = Options.Create(_options);
        var uow = AiTestData.CreateUnitOfWork(_db);
        var validator = new SlotValidator(uow);

        return new AgentService(
            _factory.Object,
            uow,
            new BookingToolsFactory(uow, AiTestData.CreateCarService(_db), Mock.Of<IKnowledgeSearch>(),
                new AgentClock(new FixedTimeProvider(UtcNow), options), options),
            new SlotFillingEngine(validator),
            validator,
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

    // ── Tool calling ─────────────────────────────────────────────────────────

    private static ChatResponse CallTool(string name, Dictionary<string, object?> arguments, string callId = "call-1") =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]));

    private static ChatResponse Say(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    private static bool HasToolResult(IList<ChatMessage> messages) =>
        messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any();

    private static string ToolResultText(IList<ChatMessage> messages) =>
        messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last().Result?.ToString() ?? "";

    /// <summary>Model that calls one tool and then answers with text.</summary>
    private FakeChatClient UseModelCallingTool(string name, Dictionary<string, object?> arguments)
    {
        var client = new FakeChatClient((messages, _) =>
            HasToolResult(messages) ? Say("Готово") : CallTool(name, arguments));
        _factory.Setup(f => f.Create(It.IsAny<string>())).Returns(client);
        return client;
    }

    [Fact]
    public async Task ChatAsync_ModelCallsSearchTool_ReturnsCarCardsAndLogsCall()
    {
        var client = UseModelCallingTool(BookingTools.SearchAvailableCarsName, new()
        {
            ["pickupDate"] = AiTestData.Pickup,
            ["returnDate"] = AiTestData.Return,
            ["location"] = "Київ",
            ["transmission"] = "Automatic"
        });

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "Автомат у Києві з 20 по 25 грудня");

        reply.Text.Should().Be("Готово");
        reply.ToolCalls.Should().ContainSingle()
            .Which.Should().Match<ToolCallRecord>(t => t.Name == BookingTools.SearchAvailableCarsName && t.Success);
        reply.Cars.Select(c => c.Id).Should().Equal(
            AiTestData.CeedId, AiTestData.CamryId, AiTestData.CrvId, AiTestData.SiennaId);

        // The model received the tool result on the second call.
        client.Calls.Should().HaveCount(2);
        ToolResultText(client.Calls[1].Messages).Should().Contain("Ceed");

        var stored = await _db.ChatMessages.SingleAsync(m => m.Role == ChatSessionMessage.AssistantRole);
        stored.ToolCallsJson.Should().Contain(BookingTools.SearchAvailableCarsName);
    }

    [Fact]
    public async Task ChatAsync_ToolReturnsError_IsRecordedAsFailedCall()
    {
        _options.SlotFilling.Enabled = false;
        UseModelCallingTool(BookingTools.SearchAvailableCarsName, new()
        {
            ["pickupDate"] = "20.12.2026",
            ["returnDate"] = AiTestData.Return
        });

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "Авто з 20.12");

        var call = reply.ToolCalls.Should().ContainSingle().Subject;
        call.Success.Should().BeFalse();
        call.Error.Should().Be(ToolErrorCodes.InvalidArguments);
        reply.Cars.Should().BeEmpty();
    }

    [Fact]
    public async Task ChatAsync_ModelOmitsRequiredArgument_WithoutSlotFilling_FailsInsideTheTool()
    {
        _options.SlotFilling.Enabled = false;
        var client = UseModelCallingTool(BookingTools.SearchAvailableCarsName, new()
        {
            ["pickupDate"] = AiTestData.Pickup
        });

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "Авто з 20 грудня");

        var call = reply.ToolCalls.Should().ContainSingle().Subject;
        call.Success.Should().BeFalse();
        call.Error.Should().Be(ToolErrorCodes.InvalidArguments);
        call.Intercepted.Should().BeFalse();
        call.ProposedMissing.Should().Equal(SlotNames.ReturnDate);   // shadow validation
        ToolResultText(client.Calls[1].Messages).Should().Contain(ToolErrorCodes.InvalidArguments);
        reply.MissingSlots.Should().BeEmpty();
    }

    [Fact]
    public async Task ChatAsync_StopsAfterMaxAgentIterations_WithSafeReply()
    {
        _options.MaxAgentIterations = 3;
        var counter = 0;
        var client = new FakeChatClient((_, _) =>
            CallTool(BookingTools.GetCarDetailsName, new() { ["carId"] = 1 }, $"call-{++counter}"));
        _factory.Setup(f => f.Create(It.IsAny<string>())).Returns(client);

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "Розкажи про авто 1");

        client.Calls.Count.Should().BeLessThanOrEqualTo(_options.MaxAgentIterations + 1);
        reply.ToolCalls.Count.Should().BeLessThanOrEqualTo(_options.MaxAgentIterations);
        reply.Text.Should().Be(AgentService.EmptyReplyText);
        reply.IsDegraded.Should().BeFalse();
    }

    [Fact]
    public async Task ChatAsync_UserIdComesFromClaimsNotFromModelArguments()
    {
        var client = UseModelCallingTool(BookingTools.GetMyRentalsName, new()
        {
            ["userId"] = "other-user"   // injected by a malicious prompt — must be ignored
        });

        await CreateSut().ChatAsync(Guid.Empty, "user-1", "Покажи мої бронювання");

        var result = ToolResultText(client.Calls[1].Messages);
        result.Should().Contain("Camry").And.Contain("Golf");
        result.Should().NotContain("Octavia").And.NotContain("Ceed");
    }

    [Fact]
    public async Task ChatAsync_AnonymousUserCannotListRentals()
    {
        var client = UseModelCallingTool(BookingTools.GetMyRentalsName, new());

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "Покажи мої бронювання");

        reply.ToolCalls.Single().Error.Should().Be(ToolErrorCodes.AuthenticationRequired);
        ToolResultText(client.Calls[1].Messages).Should().NotContain("Camry");
    }

    // ── Slot filling ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SlotFilling_MissingRequiredSlot_BlocksToolAndReportsMissingSlot()
    {
        var client = UseModelCallingTool(BookingTools.SearchAvailableCarsName, new()
        {
            ["pickupDate"] = AiTestData.Pickup
        });

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "Авто з 20 грудня");

        var call = reply.ToolCalls.Should().ContainSingle().Subject;
        call.Intercepted.Should().BeTrue();
        call.Error.Should().Be(ToolErrorCodes.InvalidSlots);
        call.ProposedMissing.Should().Equal(SlotNames.ReturnDate);
        reply.MissingSlots.Should().Equal(SlotNames.ReturnDate);
        reply.Cars.Should().BeEmpty("the tool must not run");

        var toolResult = ToolResultText(client.Calls[1].Messages);
        toolResult.Should().Contain(ToolErrorCodes.InvalidSlots).And.Contain("return date");
    }

    [Fact]
    public async Task SlotFilling_MergesSlotsFromEarlierTurns()
    {
        var turn = 0;
        var client = new FakeChatClient((messages, _) =>
        {
            if (HasToolResult(messages)) return Say("ok");
            return turn == 0
                ? CallTool(BookingTools.SearchAvailableCarsName, new()
                {
                    ["pickupDate"] = AiTestData.Pickup, ["returnDate"] = AiTestData.Return, ["category"] = "SUV"
                })
                : CallTool(BookingTools.SearchAvailableCarsName, new() { ["fuelType"] = "дизель" });
        });
        _factory.Setup(f => f.Create(It.IsAny<string>())).Returns(client);
        var sut = CreateSut();

        var first = await sut.ChatAsync(Guid.Empty, null, "SUV з 20 по 25 грудня");
        turn = 1;
        var second = await sut.ChatAsync(first.SessionId, null, "А дизельний?");

        var session = await _db.ChatSessions.SingleAsync();
        var slots = BookingSlots.FromJson(session.SlotsJson);
        slots.PickupDate.Should().Be(new DateOnly(2026, 12, 20));
        slots.Category.Should().Be("SUV");
        slots.FuelType.Should().Be(BookingSystem.Enums.FuelType.Diesel);

        var call = second.ToolCalls.Should().ContainSingle().Subject;
        call.Success.Should().BeTrue();
        call.MergedSlots.Should().BeEquivalentTo([SlotNames.PickupDate, SlotNames.ReturnDate, SlotNames.Category]);
        call.EffectiveArgumentsJson.Should().Contain("2026-12-25").And.Contain("Diesel");
        second.Cars.Select(c => c.Id).Should().Equal(AiTestData.DusterId);

        // Known parameters are given to the model at the start of the second turn.
        client.Calls[2].Options!.Instructions.Should().Contain("Known booking parameters").And.Contain("PickupDate: 2026-12-20");
    }

    [Fact]
    public async Task SlotFilling_AnyClearsAKnownFilter()
    {
        var turn = 0;
        var client = new FakeChatClient((messages, _) =>
        {
            if (HasToolResult(messages)) return Say("ok");
            return turn == 0
                ? CallTool(BookingTools.SearchAvailableCarsName, new()
                {
                    ["pickupDate"] = AiTestData.Pickup, ["returnDate"] = AiTestData.Return, ["transmission"] = "Manual"
                })
                : CallTool(BookingTools.SearchAvailableCarsName, new() { ["transmission"] = "any" });
        });
        _factory.Setup(f => f.Create(It.IsAny<string>())).Returns(client);
        var sut = CreateSut();

        var first = await sut.ChatAsync(Guid.Empty, null, "Механіка з 20 по 25 грудня");
        turn = 1;
        var second = await sut.ChatAsync(first.SessionId, null, "Коробка не важлива");

        first.Cars.Should().HaveCount(2);                 // Golf, Duster
        second.Cars.Should().HaveCount(5);                // filter dropped
        BookingSlots.FromJson((await _db.ChatSessions.SingleAsync()).SlotsJson).Transmission.Should().BeNull();
    }

    [Fact]
    public async Task SlotFilling_WeekdayContradictingUserText_IsBlockedAndModelCanSelfCorrect()
    {
        // Today is Friday 2026-11-20; "з понеділка" = Monday 2026-11-23, not Wednesday 2026-11-25.
        var attempt = 0;
        var client = new FakeChatClient((messages, _) =>
        {
            var results = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Count();
            if (results == 2) return Say("ok");
            attempt++;
            return attempt == 1
                ? CallTool(BookingTools.SearchAvailableCarsName, new() { ["pickupDate"] = "2026-11-25", ["returnDate"] = "2026-11-28" }, "c1")
                : CallTool(BookingTools.SearchAvailableCarsName, new() { ["pickupDate"] = "2026-11-23", ["returnDate"] = "2026-11-26" }, "c2");
        });
        _factory.Setup(f => f.Create(It.IsAny<string>())).Returns(client);

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "Потрібне авто з понеділка на 3 дні");

        reply.ToolCalls.Should().HaveCount(2);
        reply.ToolCalls[0].Intercepted.Should().BeTrue();
        reply.ToolCalls[0].ProposedInvalid.Should().ContainSingle(i => i.Contains("Monday"));
        reply.ToolCalls[1].Success.Should().BeTrue();
        reply.ToolCalls[1].ProposedValid.Should().BeTrue();
        reply.Cars.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SlotFillingDisabled_ExecutesWrongDatesAndKeepsNoSlots()
    {
        _options.SlotFilling.Enabled = false;
        var client = UseModelCallingTool(BookingTools.SearchAvailableCarsName, new()
        {
            ["pickupDate"] = "2026-11-25", ["returnDate"] = "2026-11-28"
        });

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "Потрібне авто з понеділка на 3 дні");

        var call = reply.ToolCalls.Should().ContainSingle().Subject;
        call.Success.Should().BeTrue("baseline runs the tool with the model's arguments as is");
        call.Intercepted.Should().BeFalse();
        call.ProposedValid.Should().BeFalse("shadow validation still records the wrong weekday");
        call.EffectiveArgumentsJson.Should().BeNull();

        (await _db.ChatSessions.SingleAsync()).SlotsJson.Should().Be("{}");
        client.Calls[0].Options!.Instructions.Should().NotContain("Known booking parameters");
    }

    [Fact]
    public async Task SlotFilling_PerCallOverrideDisablesIt()
    {
        UseModelCallingTool(BookingTools.SearchAvailableCarsName, new() { ["pickupDate"] = AiTestData.Pickup });

        var reply = await CreateSut().ChatAsync(Guid.Empty, null, "Авто з 20 грудня",
            new AgentChatOptions { SlotFillingEnabled = false });

        reply.ToolCalls.Single().Intercepted.Should().BeFalse();
        reply.ToolCalls.Single().Error.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    // ── History for the widget ───────────────────────────────────────────────

    [Fact]
    public async Task History_ReturnsMessagesWithoutInternalNotes()
    {
        UseModelReplying("Вітаю!");
        var sut = CreateSut();
        var sessionId = (await sut.ChatAsync(Guid.Empty, "user-1", "Привіт")).SessionId;
        _db.ChatMessages.Add(new ChatSessionMessage
        {
            SessionId = sessionId, Role = ChatSessionMessage.AssistantRole,
            Content = "[Booking confirmed by the user: rental #1.]"
        });
        await _db.SaveChangesAsync();

        var history = await sut.GetHistoryAsync(sessionId, "user-1");

        history!.Messages.Select(m => (m.Role, m.Content)).Should().Equal(
            (ChatSessionMessage.UserRole, "Привіт"),
            (ChatSessionMessage.AssistantRole, "Вітаю!"));
    }

    [Fact]
    public async Task History_OfAnotherUsersSession_IsNotReturned()
    {
        UseModelReplying("ok");
        var sut = CreateSut();
        var sessionId = (await sut.ChatAsync(Guid.Empty, "user-1", "Мої дані")).SessionId;

        (await sut.GetHistoryAsync(sessionId, "other-user")).Should().BeNull();
        (await sut.GetHistoryAsync(sessionId, null)).Should().BeNull();
        (await sut.GetHistoryAsync(Guid.NewGuid(), "user-1")).Should().BeNull();
    }

    [Fact]
    public async Task History_IncludesPendingDraftUntilItExpires()
    {
        var client = UseModelCallingTool(BookingTools.CreateRentalDraftName, new()
        {
            ["carId"] = AiTestData.CrvId, ["pickupDate"] = AiTestData.Pickup, ["returnDate"] = AiTestData.Return
        });
        var sut = CreateSut();
        var reply = await sut.ChatAsync(Guid.Empty, "user-1", "Бронюй Honda CR-V з 20 по 25 грудня");

        var history = await sut.GetHistoryAsync(reply.SessionId, "user-1");

        history!.PendingDraft.Should().NotBeNull();
        history.PendingDraft!.PendingActionId.Should().Be(reply.PendingActionId!.Value);

        var action = await _db.PendingActions.SingleAsync();
        action.ExpiresAt = UtcNow.UtcDateTime.AddMinutes(-1);
        await _db.SaveChangesAsync();

        (await sut.GetHistoryAsync(reply.SessionId, "user-1"))!.PendingDraft.Should().BeNull();
    }
}
