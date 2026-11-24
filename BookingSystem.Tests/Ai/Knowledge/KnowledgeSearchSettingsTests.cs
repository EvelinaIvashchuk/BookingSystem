using BookingSystem.Ai;
using BookingSystem.Ai.Knowledge;
using BookingSystem.Ai.Tools;
using BookingSystem.Data;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using static BookingSystem.Tests.Ai.AiTestData;

namespace BookingSystem.Tests.Ai.Knowledge;

public class KnowledgeSearchSettingsTests
{
    private static KnowledgeSearchSettings Create(ApplicationDbContext? db = null, KnowledgeSearchMode configured = KnowledgeSearchMode.HybridAdaptive) =>
        new(db ?? TestDbContextFactory.Create(), Options.Create(new AiOptions { Search = { Mode = configured } }), TimeProvider.System);

    [Fact]
    public void AllFourModesCanBeChosen()
    {
        Create().GetAvailableModes().Should().BeEquivalentTo(
            [KnowledgeSearchMode.Vector, KnowledgeSearchMode.FullText, KnowledgeSearchMode.HybridRrf, KnowledgeSearchMode.HybridAdaptive]);
    }

    [Theory]
    [InlineData(KnowledgeSearchMode.HybridAdaptive)]
    [InlineData(KnowledgeSearchMode.Vector)]
    public async Task WithoutChoice_UsesTheConfiguredMode(KnowledgeSearchMode configured)
    {
        (await Create(configured: configured).GetActiveModeAsync()).Should().Be(configured);
    }

    [Fact]
    public async Task Choice_IsStoredAndSharedBetweenInstances_WithASingleRow()
    {
        var db = TestDbContextFactory.Create();

        await Create(db).SetActiveModeAsync(KnowledgeSearchMode.FullText);
        await Create(db).SetActiveModeAsync(KnowledgeSearchMode.Vector);

        (await Create(db).GetActiveModeAsync()).Should().Be(KnowledgeSearchMode.Vector);
        db.AppSettings.Count().Should().Be(1);
    }

    [Fact]
    public async Task UndefinedMode_IsRejected_AndTheChoiceStaysUnchanged()
    {
        var db = TestDbContextFactory.Create();
        var sut = Create(db);
        await sut.SetActiveModeAsync(KnowledgeSearchMode.HybridRrf);

        var act = () => sut.SetActiveModeAsync((KnowledgeSearchMode)42);

        await act.Should().ThrowAsync<ArgumentException>();
        (await sut.GetActiveModeAsync()).Should().Be(KnowledgeSearchMode.HybridRrf);
    }

    [Fact]
    public async Task UnreadableStoredValue_FallsBackToTheConfiguredMode()
    {
        var db = TestDbContextFactory.Create();
        db.AppSettings.Add(new BookingSystem.Models.AppSetting { Key = KnowledgeSearchSettings.SettingKey, Value = "Telepathy", UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        (await Create(db, KnowledgeSearchMode.Vector).GetActiveModeAsync()).Should().Be(KnowledgeSearchMode.Vector);
    }

    [Theory]
    [InlineData(KnowledgeSearchMode.FullText)]
    [InlineData(KnowledgeSearchMode.Vector)]
    public async Task PolicySearchTool_UsesTheModeChosenByAdministrator(KnowledgeSearchMode chosen)
    {
        var knowledge = new Mock<IKnowledgeSearch>();
        knowledge.Setup(k => k.SearchAsync("Який депозит?", 3, chosen, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeSearchResult(chosen, [new KnowledgeHit("deposit#1", "deposit", "Що таке депозит", "Депозит — це сума ...", 0.02)]));
        var settings = new Mock<IKnowledgeSearchSettings>();
        settings.Setup(s => s.GetActiveModeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(chosen);
        using var db = TestDbContextFactory.Create();
        var tools = new BookingTools(CreateUnitOfWork(db), CreateCarService(db), knowledge.Object, CreateClock(), new AiOptions(),
            new AgentTurnContext(Guid.NewGuid(), null, Today), searchSettings: settings.Object);

        var result = (PolicySearchResult)await tools.SearchRentalPoliciesAsync("Який депозит?");

        result.Fragments.Select(f => f.Source).Should().Equal("deposit#1");
        knowledge.Verify(k => k.SearchAsync("Який депозит?", 3, chosen, It.IsAny<CancellationToken>()), Times.Once);
    }
}
