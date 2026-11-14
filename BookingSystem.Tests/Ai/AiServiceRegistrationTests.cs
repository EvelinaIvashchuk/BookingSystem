using BookingSystem.Ai;
using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Services;
using BookingSystem.Services.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BookingSystem.Tests.Ai;

public class AiServiceRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<IRentalRepository, RentalRepository>();
        services.AddScoped<ICarRepository, CarRepository>();
        services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
        services.AddScoped<IPendingActionRepository, PendingActionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICarService, CarService>();
        services.AddAiAgent(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void LegacyGroqKey_IsUsedWhenNewKeyIsEmpty()
    {
        using var provider = Build(new()
        {
            ["Groq:ApiKey"] = "legacy-key",
            ["Ai:Providers:groq:Endpoint"] = "https://api.groq.com/openai/v1",
            ["Ai:Providers:groq:ApiKey"] = ""
        });

        var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;

        options.Providers["groq"].ApiKey.Should().Be("legacy-key");
    }

    [Fact]
    public void NewKey_TakesPrecedenceOverLegacyKey()
    {
        using var provider = Build(new()
        {
            ["Groq:ApiKey"] = "legacy-key",
            ["Ai:Providers:groq:Endpoint"] = "https://api.groq.com/openai/v1",
            ["Ai:Providers:groq:ApiKey"] = "new-key"
        });

        var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;

        options.Providers["groq"].ApiKey.Should().Be("new-key");
    }

    [Fact]
    public async Task AgentService_ResolvesAndDegradesWithoutKeys()
    {
        using var provider = Build(new()
        {
            ["Ai:DefaultModel"] = "groq:llama-3.3-70b-versatile",
            ["Ai:Providers:groq:Endpoint"] = "https://api.groq.com/openai/v1"
        });
        await using var scope = provider.CreateAsyncScope();

        var agent = scope.ServiceProvider.GetRequiredService<IAgentService>();
        var reply = await agent.ChatAsync(Guid.NewGuid(), null, "hi");

        reply.IsDegraded.Should().BeTrue();
        reply.Text.Should().Be(AgentService.NotConfiguredText);
    }

    [Fact]
    public void Pricing_BindsModelIdsContainingColons()
    {
        using var provider = Build(new()
        {
            ["Ai:Pricing:0:Model"] = "groq:openai/gpt-oss-120b",
            ["Ai:Pricing:0:InputPer1M"] = "0.15",
            ["Ai:Pricing:0:OutputPer1M"] = "0.75"
        });

        var pricing = provider.GetRequiredService<IOptions<AiOptions>>().Value.Pricing;

        pricing.Should().ContainSingle(p => p.Model == "groq:openai/gpt-oss-120b" && p.InputPer1M == 0.15m);
    }
}
