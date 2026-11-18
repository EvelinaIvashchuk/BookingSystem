using BookingSystem.Ai.Knowledge;
using BookingSystem.Evaluation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Experiments for the coursework (see results/README.md):
//   dotnet run --project BookingSystem.Evaluation -- index [--force]
//   dotnet run --project BookingSystem.Evaluation -- agent --models groq:openai/gpt-oss-120b,gemini:gemini-3.5-flash-lite --repeats 3
//   dotnet run --project BookingSystem.Evaluation -- ablation --model groq:openai/gpt-oss-120b --model groq:openai/gpt-oss-20b --repeats 3
//   dotnet run --project BookingSystem.Evaluation -- retrieval --tune-on dev --eval-on test
//   dotnet run --project BookingSystem.Evaluation -- summary
// Options: --types simple,faq  --ids S-01,F-02  --delay-ms 2000  --out results/x.csv  (+ any --Ai:... config override)

var command = args.FirstOrDefault()?.ToLowerInvariant();
if (command is null or "help" or "--help")
{
    Console.WriteLine("Commands: index | agent | ablation | retrieval | summary  (see Program.cs for options)");
    return 0;
}

using var host = EvalHost.Build(args);
var services = host.Services;
var paths = services.GetRequiredService<EvalPaths>();
var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("BookingSystem.Evaluation");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await using (var scope = services.CreateAsyncScope())
        await ActivatorUtilities.CreateInstance<EvalDatabase>(scope.ServiceProvider).PrepareAsync(cts.Token);

    switch (command)
    {
        case "index":
        {
            await using var scope = services.CreateAsyncScope();
            var indexer = scope.ServiceProvider.GetRequiredService<KnowledgeIndexer>();
            var report = await indexer.IndexAsync(paths.KnowledgeBase, force: Has("--force"), cts.Token);
            logger.LogInformation("Indexed: {Documents} documents ({Indexed} indexed, {Skipped} unchanged), {Chunks} chunks, embeddings: {Embeddings}",
                report.Documents, report.Indexed, report.Skipped, report.Chunks, report.WithEmbeddings ? report.EmbeddingModel : "MISSING");
            break;
        }

        case "agent":
        {
            var models = Values("--models").SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToList();
            if (models.Count == 0) throw new ArgumentException("--models is required");
            var slotModes = Value("--slot-filling") switch { "off" => new[] { false }, "both" => [true, false], _ => [true] };
            var output = OutputPath("agent_runs.csv");
            await ActivatorUtilities.CreateInstance<AgentExperiment>(services).RunAsync(
                new AgentRunPlan("E1", models, Int("--repeats", 3), slotModes, output, Set("--types"), Set("--ids"), NullableInt("--delay-ms")), cts.Token);
            AgentSummary.Write(output, Path.GetDirectoryName(output)!);
            break;
        }

        case "ablation":
        {
            var models = Values("--model").Concat(Values("--models").SelectMany(v => v.Split(','))).Distinct().ToList();
            if (models.Count == 0) throw new ArgumentException("--model is required");
            var output = OutputPath("agent_runs.csv");
            await ActivatorUtilities.CreateInstance<AgentExperiment>(services).RunAsync(
                new AgentRunPlan("E2", models, Int("--repeats", 3), [true, false], output, Set("--types"), Set("--ids"), NullableInt("--delay-ms")), cts.Token);
            AgentSummary.Write(output, Path.GetDirectoryName(output)!);
            break;
        }

        case "retrieval":
            await ActivatorUtilities.CreateInstance<RetrievalExperiment>(services)
                .RunAsync(Value("--tune-on") ?? "dev", Value("--eval-on") ?? "test", cts.Token);
            break;

        case "summary":
        {
            var runs = OutputPath("agent_runs.csv");
            AgentSummary.Write(runs, Path.GetDirectoryName(runs)!);
            logger.LogInformation("Summaries written next to {Runs}", runs);
            break;
        }

        default:
            Console.Error.WriteLine($"Unknown command '{command}'.");
            return 1;
    }
    return 0;
}
catch (OperationCanceledException)
{
    logger.LogWarning("Cancelled. Completed rows are saved; re-run the same command to resume.");
    return 2;
}

// ── argument helpers ──────────────────────────────────────────────────────────
bool Has(string name) => args.Contains(name, StringComparer.OrdinalIgnoreCase);

IEnumerable<string> Values(string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            yield return args[i + 1];
}

string? Value(string name) => Values(name).LastOrDefault();
int Int(string name, int fallback) => int.TryParse(Value(name), out var v) ? v : fallback;
int? NullableInt(string name) => int.TryParse(Value(name), out var v) ? v : null;
IReadOnlySet<string>? Set(string name) =>
    Value(name) is { } v ? v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet() : null;
string OutputPath(string fileName) =>
    Value("--out") is { } custom ? Path.GetFullPath(custom, paths.Project) : Path.Combine(paths.Results, fileName);
