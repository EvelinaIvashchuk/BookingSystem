using BookingSystem.Ai.Knowledge;
using BookingSystem.Evaluation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Experiments for the coursework (see results/README.md):
//   dotnet run --project BookingSystem.Evaluation -- index [--force]
//   dotnet run --project BookingSystem.Evaluation -- agent --models groq:openai/gpt-oss-120b,gemini:gemini-3.5-flash-lite --repeats 3
//   dotnet run --project BookingSystem.Evaluation -- ablation --model gemini:gemini-3.8-flash --model gemini:gemini-3.5-flash-lite --repeats 3
//   dotnet run --project BookingSystem.Evaluation -- retrieval --tune-on dev --eval-on test
//   dotnet run --project BookingSystem.Evaluation -- summary
//   dotnet run --project BookingSystem.Evaluation -- rescore      (re-apply the clarification criterion to stored rows)
// Options: --types simple,faq  --ids S-01,F-02  --delay-ms 2000  --out results/x.csv  --max-cost 4.80
//          model@N sets repeats per model, e.g. gemini:gemini-3.8-flash@3   (+ any --Ai:... config override)

var command = args.FirstOrDefault()?.ToLowerInvariant();
if (command is null or "help" or "--help")
{
    Console.WriteLine("Commands: index | agent | ablation | retrieval | summary | rescore  (see Program.cs for options)");
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
    // summary / rescore only read the CSV: preparing the database would wipe chat sessions of a run in progress.
    if (command is not ("summary" or "rescore"))
    {
        await using var scope = services.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<EvalDatabase>(scope.ServiceProvider).PrepareAsync(cts.Token);
    }

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
            var models = Models("--models");
            if (models.Count == 0) throw new ArgumentException("--models is required");
            var slotModes = Value("--slot-filling") switch { "off" => new[] { false }, "both" => [true, false], _ => [true] };
            var output = OutputPath("agent_runs.csv");
            await ActivatorUtilities.CreateInstance<AgentExperiment>(services).RunAsync(
                new AgentRunPlan("E1", models, slotModes, output, Set("--types"), Set("--ids"), NullableInt("--delay-ms"), MaxCost()), cts.Token);
            AgentSummary.Write(output, Path.GetDirectoryName(output)!);
            break;
        }

        case "ablation":
        {
            var models = Models("--model").Concat(Models("--models")).DistinctBy(m => m.Model).ToList();
            if (models.Count == 0) throw new ArgumentException("--model is required");
            // By default only "slot filling off" is run: the "on" condition is the same model and scenarios as E1,
            // so its E1 rows are reused in the E2 summary. --both runs both conditions anew.
            bool[] modes = Has("--both") ? [true, false] : [false];
            var output = OutputPath("agent_runs.csv");
            await ActivatorUtilities.CreateInstance<AgentExperiment>(services).RunAsync(
                new AgentRunPlan("E2", models, modes, output, Set("--types"), Set("--ids"), NullableInt("--delay-ms"), MaxCost()), cts.Token);
            AgentSummary.Write(output, Path.GetDirectoryName(output)!);
            break;
        }

        case "retrieval":
            await ActivatorUtilities.CreateInstance<RetrievalExperiment>(services)
                .RunAsync(Value("--tune-on") ?? "dev", Value("--eval-on") ?? "test", cts.Token);
            break;

        case "rescore":
        {
            // Re-applies the current clarification criterion to stored rows (see AgentRescore), then rebuilds summaries.
            var runs = OutputPath("agent_runs.csv");
            var result = AgentRescore.Run(runs, Path.Combine(paths.Datasets, "agent_scenarios.json"));
            logger.LogInformation("Rescored {Rows} rows: clarification changed in {Clarification}, task success in {Success}",
                result.Rows, result.ClarificationChanged, result.SuccessChanged);
            AgentSummary.Write(runs, Path.GetDirectoryName(runs)!);
            break;
        }

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
List<ModelRun> Models(string name) =>
    Values(name).SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Select(spec => ModelRun.Parse(spec, Int("--repeats", 3))).ToList();
decimal? MaxCost() =>
    decimal.TryParse(Value("--max-cost"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
int Int(string name, int fallback) => int.TryParse(Value(name), out var v) ? v : fallback;
int? NullableInt(string name) => int.TryParse(Value(name), out var v) ? v : null;
IReadOnlySet<string>? Set(string name) =>
    Value(name) is { } v ? v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet() : null;
string OutputPath(string fileName) =>
    Value("--out") is { } custom ? Path.GetFullPath(custom, paths.Project) : Path.Combine(paths.Results, fileName);
