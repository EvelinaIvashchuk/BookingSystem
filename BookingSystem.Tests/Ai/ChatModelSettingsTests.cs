using BookingSystem.Ai;
using BookingSystem.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BookingSystem.Tests.Ai;

public class ChatModelSettingsTests
{
    private static AiOptions Options() => new()
    {
        DefaultModel = "gemini:flash",
        Providers =
        {
            ["gemini"] = new AiProviderOptions { Endpoint = "https://g.example/", ApiKey = "k1" },
            ["groq"] = new AiProviderOptions { Endpoint = "https://q.example/", ApiKey = "k2" },
            ["nokey"] = new AiProviderOptions { Endpoint = "https://n.example/", ApiKey = "" }
        },
        Pricing =
        [
            new AiModelPricing { Model = "gemini:flash", InputPer1M = 0.75m, OutputPer1M = 3.75m },
            new AiModelPricing { Model = "groq:openai/gpt-oss-120b" },
            new AiModelPricing { Model = "nokey:model", InputPer1M = 1 },
            new AiModelPricing { Model = "unknown:model" },
            new AiModelPricing { Model = "broken" }
        ]
    };

    private static ChatModelSettings Create(AiOptions? options = null, string? dbName = null) =>
        new(TestDbContextFactory.Create(dbName), Microsoft.Extensions.Options.Options.Create(options ?? Options()), TimeProvider.System);

    [Fact]
    public void GetAvailableModels_ListsDefaultFirst_WithoutDuplicatesAndModelsWithoutKey()
    {
        var models = Create().GetAvailableModels();

        models.Select(m => m.Id).Should().Equal("gemini:flash", "groq:openai/gpt-oss-120b");
        models[0].InputPer1M.Should().Be(0.75m);
        models[0].OutputPer1M.Should().Be(3.75m);
        models[1].Model.Should().Be("openai/gpt-oss-120b");
    }

    [Fact]
    public async Task GetActiveModelAsync_WithoutChoice_ReturnsDefault()
    {
        (await Create().GetActiveModelAsync()).Should().Be("gemini:flash");
    }

    [Fact]
    public async Task SetActiveModelAsync_StoresChoice_AndIsSharedBetweenInstances()
    {
        var db = TestDbContextFactory.Create();
        var options = Microsoft.Extensions.Options.Options.Create(Options());
        await new ChatModelSettings(db, options, TimeProvider.System).SetActiveModelAsync("groq:openai/gpt-oss-120b");

        (await new ChatModelSettings(db, options, TimeProvider.System).GetActiveModelAsync())
            .Should().Be("groq:openai/gpt-oss-120b");
    }

    [Fact]
    public async Task SetActiveModelAsync_ChangingTheChoiceUpdatesTheSingleRow()
    {
        var db = TestDbContextFactory.Create();
        var sut = new ChatModelSettings(db, Microsoft.Extensions.Options.Options.Create(Options()), TimeProvider.System);

        await sut.SetActiveModelAsync("groq:openai/gpt-oss-120b");
        await sut.SetActiveModelAsync("gemini:flash");

        db.AppSettings.Count().Should().Be(1);
        (await sut.GetActiveModelAsync()).Should().Be("gemini:flash");
    }

    [Theory]
    [InlineData("nokey:model")]      // provider without an API key
    [InlineData("unknown:model")]    // provider not configured
    [InlineData("gemini:other")]     // not in the list
    [InlineData("")]
    [InlineData("broken")]
    public async Task SetActiveModelAsync_RejectsModelsThatAreNotAvailable(string id)
    {
        var sut = Create();

        var act = () => sut.SetActiveModelAsync(id);

        await act.Should().ThrowAsync<ArgumentException>();
        (await sut.GetActiveModelAsync()).Should().Be("gemini:flash");
    }

    [Fact]
    public async Task GetActiveModelAsync_FallsBackToDefault_WhenChosenModelLostItsKey()
    {
        var db = TestDbContextFactory.Create();
        var options = Options();
        await new ChatModelSettings(db, Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System)
            .SetActiveModelAsync("groq:openai/gpt-oss-120b");

        options.Providers["groq"].ApiKey = "";   // key removed from the configuration

        (await new ChatModelSettings(db, Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System).GetActiveModelAsync())
            .Should().Be("gemini:flash");
    }
}
