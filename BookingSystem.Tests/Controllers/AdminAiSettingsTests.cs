using BookingSystem.Ai;
using BookingSystem.Controllers;
using BookingSystem.Resources;
using BookingSystem.Services.Interfaces;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Moq;

namespace BookingSystem.Tests.Controllers;

public class AdminAiSettingsTests
{
    private readonly Mock<IChatModelSettings> _chat = new();
    private readonly Mock<IRankingModelSettings> _ranking = new();
    private readonly Mock<IKnowledgeSearchSettings> _search = new();

    private AdminController CreateSut()
    {
        _chat.Setup(c => c.GetAvailableModels()).Returns(
        [
            new ChatModelOption("gemini:flash", "gemini", "flash", 0.75m, 3.75m),
            new ChatModelOption("groq:oss", "groq", "oss", 0, 0)
        ]);
        _chat.Setup(c => c.GetActiveModelAsync(It.IsAny<CancellationToken>())).ReturnsAsync("gemini:flash");
        _ranking.Setup(r => r.GetAvailableModels()).Returns(
        [
            new RankingModelOption("lightgbm", 0.895), new RankingModelOption("mlp", 0.945), new RankingModelOption("two_tower", 0.962)
        ]);
        _ranking.Setup(r => r.GetActiveModelAsync(It.IsAny<CancellationToken>())).ReturnsAsync("lightgbm");
        _search.Setup(s => s.GetActiveModeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(KnowledgeSearchMode.HybridAdaptive);

        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

        var controller = new AdminController(Mock.Of<IRentalService>(), Mock.Of<ICarService>(), Mock.Of<IUserService>(),
            Mock.Of<IStorageService>(), TestDbContextFactory.Create(), localizer.Object,
            _chat.Object, _ranking.Object, _search.Object, Options.Create(new AiOptions()));
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
        return controller;
    }

    [Fact]
    public async Task Save_StoresOnlyTheValuesThatChanged()
    {
        var sut = CreateSut();

        var result = await sut.SaveAiSettings("gemini:flash", "two_tower", nameof(KnowledgeSearchMode.HybridAdaptive));

        result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
        _chat.Verify(c => c.SetActiveModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _ranking.Verify(r => r.SetActiveModelAsync("two_tower", It.IsAny<CancellationToken>()), Times.Once);
        _search.Verify(s => s.SetActiveModeAsync(It.IsAny<KnowledgeSearchMode>(), It.IsAny<CancellationToken>()), Times.Never);
        sut.TempData["Success"].Should().Be("Msg_AiSettingsSaved");
    }

    [Fact]
    public async Task Save_AllThreeChanged_StoresAllThree()
    {
        var sut = CreateSut();

        await sut.SaveAiSettings("groq:oss", "mlp", nameof(KnowledgeSearchMode.Vector));

        _chat.Verify(c => c.SetActiveModelAsync("groq:oss", It.IsAny<CancellationToken>()), Times.Once);
        _ranking.Verify(r => r.SetActiveModelAsync("mlp", It.IsAny<CancellationToken>()), Times.Once);
        _search.Verify(s => s.SetActiveModeAsync(KnowledgeSearchMode.Vector, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Save_WithoutRankingModels_LeavesRankingUntouched()
    {
        var sut = CreateSut();

        await sut.SaveAiSettings("groq:oss", null, nameof(KnowledgeSearchMode.FullText));

        _ranking.Verify(r => r.SetActiveModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _chat.Verify(c => c.SetActiveModelAsync("groq:oss", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("evil:model", "mlp", "Vector")]          // unknown chat model
    [InlineData("groq:oss", "gpt-5", "Vector")]          // unknown ranking model
    [InlineData("groq:oss", "mlp", "Telepathy")]         // unknown search mode
    [InlineData("groq:oss", "mlp", "42")]                // numeric value outside the enum
    [InlineData("", "mlp", "Vector")]
    public async Task Save_WithAnyInvalidValue_StoresNothing(string chat, string ranking, string search)
    {
        var sut = CreateSut();

        var result = await sut.SaveAiSettings(chat, ranking, search);

        result.Should().BeOfType<RedirectToActionResult>();
        sut.TempData["Error"].Should().Be("Msg_AiSettingsInvalid");
        _chat.Verify(c => c.SetActiveModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _ranking.Verify(r => r.SetActiveModelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _search.Verify(s => s.SetActiveModeAsync(It.IsAny<KnowledgeSearchMode>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
