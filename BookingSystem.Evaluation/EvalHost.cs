using BookingSystem.Ai;
using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Services;
using BookingSystem.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookingSystem.Evaluation;

/// <summary>
/// Builds the same agent/search services as the web application, but against the separate
/// evaluation database and with a per-scope clock that each scenario can pin.
/// Configuration: BookingSystem/appsettings.json → BookingSystem.Evaluation/appsettings.json →
/// user secrets (shared with the web project) → environment → command line.
/// </summary>
public static class EvalHost
{
    public static IHost Build(string[] args)
    {
        var root = RepoRoot.Find();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = Path.Combine(root, "BookingSystem.Evaluation"),
            EnvironmentName = "Evaluation"
        });

        builder.Configuration
            .AddJsonFile(Path.Combine(root, "BookingSystem", "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(root, "BookingSystem.Evaluation", "appsettings.json"), optional: false)
            .AddUserSecrets(typeof(EvalHost).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args.Where(a => a.StartsWith("--Ai:") || a.StartsWith("--Evaluation:") || a.StartsWith("--ConnectionStrings:")).ToArray());

        builder.Logging
            .AddConfiguration(builder.Configuration.GetSection("Logging"))
            .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });

        var connectionString = builder.Configuration.GetConnectionString("Eval")
            ?? throw new InvalidOperationException("ConnectionStrings:Eval is not configured.");

        var services = builder.Services;
        services.AddDbContext<ApplicationDbContext>(o => o.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0))));
        services.AddScoped<IRentalRepository, RentalRepository>();
        services.AddScoped<ICarRepository, CarRepository>();
        services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
        services.AddScoped<IPendingActionRepository, PendingActionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICarService, CarService>();

        services.AddAiAgent(builder.Configuration);

        // Each scenario runs in its own scope with its own "now" (relative dates must be reproducible).
        services.AddScoped<ScenarioTimeProvider>();
        services.Replace(ServiceDescriptor.Scoped<TimeProvider>(sp => sp.GetRequiredService<ScenarioTimeProvider>()));
        services.Replace(ServiceDescriptor.Scoped<AgentClock, AgentClock>());

        services.Configure<EvaluationOptions>(builder.Configuration.GetSection("Evaluation"));
        services.AddSingleton(new EvalPaths(root));

        return builder.Build();
    }
}

public sealed class EvaluationOptions
{
    /// <summary>Default local (Europe/Kyiv) "now" of scenarios, e.g. 2026-11-18T10:00:00 (Wednesday).</summary>
    public string Now { get; set; } = "2026-11-18T10:00:00";
    public int DelayBetweenTurnsMs { get; set; } = 1500;
    public int MaxScenarioAttempts { get; set; } = 6;
}

public sealed record EvalPaths(string Root)
{
    public string Project => Path.Combine(Root, "BookingSystem.Evaluation");
    public string Datasets => Path.Combine(Project, "datasets");
    public string Results => Path.Combine(Project, "results");
    public string KnowledgeBase => Path.Combine(Root, "BookingSystem", "KnowledgeBase");
}

public static class RepoRoot
{
    public static string Find()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "BookingSystem.sln")))
                    return dir.FullName;
        }
        throw new InvalidOperationException("Repository root (BookingSystem.sln) not found.");
    }
}

/// <summary>Clock pinned to the scenario's local time (Europe/Kyiv).</summary>
public sealed class ScenarioTimeProvider : TimeProvider
{
    private static readonly TimeZoneInfo Kyiv =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Kyiv", out var z) ? z
        : TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Kiev", out var legacy) ? legacy
        : TimeZoneInfo.Utc;

    private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

    public void SetLocalNow(DateTime localKyiv) =>
        _utcNow = new DateTimeOffset(localKyiv, Kyiv.GetUtcOffset(localKyiv)).ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;
}
