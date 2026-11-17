using System.Reflection;
using System.Security.Claims;
using BookingSystem.Ai;
using BookingSystem.Ai.Dtos;
using BookingSystem.Controllers;
using BookingSystem.Resources;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Moq;

namespace BookingSystem.Tests.Controllers;

public class AiControllerTests
{
    private const string UserId = "user-1";

    private readonly Mock<IAgentService> _agent = new();
    private readonly Mock<IPendingActionService> _pendingActions = new();
    private readonly Mock<IStringLocalizer<SharedResources>> _localizer = new();

    public AiControllerTests()
    {
        _localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(key => new LocalizedString(key, key));
    }

    private AiController CreateSut(string? userId = UserId)
    {
        var sut = new AiController(_agent.Object, _pendingActions.Object, Options.Create(new AiOptions()), _localizer.Object);

        var identity = userId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test");

        sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        sut.Url = Mock.Of<IUrlHelper>();
        return sut;
    }

    private void Outcome(PendingActionOutcomeStatus status, int? rentalId = null) =>
        _pendingActions.Setup(p => p.ConfirmAsync(It.IsAny<Guid>(), UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingActionOutcome(status, RentalId: rentalId));

    // ── Confirm ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_OwnDraft_ReturnsOkWithRentalId()
    {
        Outcome(PendingActionOutcomeStatus.Confirmed, rentalId: 42);

        var result = await CreateSut().Confirm(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeEquivalentTo(new { ok = true, rentalId = 42 });
    }

    [Fact]
    public async Task Confirm_DraftOfAnotherUser_Returns403()
    {
        Outcome(PendingActionOutcomeStatus.Forbidden);

        var result = await CreateSut().Confirm(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Confirm_ExpiredDraft_Returns410()
    {
        Outcome(PendingActionOutcomeStatus.Expired);

        var result = await CreateSut().Confirm(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status410Gone);
    }

    [Fact]
    public async Task Confirm_AlreadyProcessed_Returns409()
    {
        Outcome(PendingActionOutcomeStatus.AlreadyProcessed);

        var result = await CreateSut().Confirm(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Confirm_PassesUserIdFromClaims()
    {
        Outcome(PendingActionOutcomeStatus.Confirmed, rentalId: 1);
        var id = Guid.NewGuid();

        await CreateSut().Confirm(id, CancellationToken.None);

        _pendingActions.Verify(p => p.ConfirmAsync(id, UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(nameof(AiController.Confirm))]
    [InlineData(nameof(AiController.Cancel))]
    public void DecisionEndpoints_RequireAuthAntiForgeryAndPost(string action)
    {
        var method = typeof(AiController).GetMethod(action)!;

        method.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
    }

    // ── Chat ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Chat_TooLongMessage_IsRejectedWithoutCallingTheModel()
    {
        var result = await CreateSut().Chat(new ChatRequest { Message = new string('a', 1001) }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        _agent.Verify(a => a.ChatAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<AgentChatOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Chat_Anonymous_PassesNullUserId()
    {
        _agent.Setup(a => a.ChatAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<AgentChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentReply { Text = "ok", SessionId = Guid.NewGuid() });

        await CreateSut(userId: null).Chat(new ChatRequest { Message = "hi" }, CancellationToken.None);

        _agent.Verify(a => a.ChatAsync(Guid.Empty, null, "hi",
            It.IsAny<AgentChatOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── History ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task History_UnknownOrForeignSession_Returns404()
    {
        _agent.Setup(a => a.GetHistoryAsync(It.IsAny<Guid>(), UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatHistory?)null);

        var result = await CreateSut().History(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task History_ReturnsMessagesForTheWidget()
    {
        var sessionId = Guid.NewGuid();
        _agent.Setup(a => a.GetHistoryAsync(sessionId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatHistory(sessionId, [new ChatHistoryMessage("user", "Привіт", DateTime.UtcNow)], null));

        var result = await CreateSut().History(sessionId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeEquivalentTo(new { sessionId, messages = new[] { new { role = "user", text = "Привіт" } } });
    }
}
