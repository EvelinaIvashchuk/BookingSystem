using System.Text.Json;
using BookingSystem.Ai.Knowledge;
using BookingSystem.Ai.Slots;
using BookingSystem.Data;
using BookingSystem.Evaluation;
using FluentAssertions;

namespace BookingSystem.Tests.Evaluation;

public class DatasetIntegrityTests
{
    private static readonly string Root = RepoRoot.Find();
    private static readonly ScenarioFile Scenarios =
        Datasets.LoadScenarios(Path.Combine(Root, "BookingSystem.Evaluation", "datasets", "agent_scenarios.json"));
    private static readonly QrelsFile Qrels =
        Datasets.LoadQrels(Path.Combine(Root, "BookingSystem.Evaluation", "datasets", "retrieval_qrels.json"));

    private static readonly string[] Tools =
        ["search_available_cars", "get_car_details", "calculate_price", "get_my_rentals", "create_rental_draft", "search_rental_policies"];

    [Fact]
    public void Scenarios_FollowThePlannedMix()
    {
        Scenarios.Scenarios.Should().HaveCount(74);
        Scenarios.Scenarios.Select(s => s.Id).Should().OnlyHaveUniqueItems();
        Scenarios.Scenarios.GroupBy(s => s.Type).ToDictionary(g => g.Key, g => g.Count()).Should().BeEquivalentTo(
            new Dictionary<string, int>
            {
                ["simple"] = 15, ["constraints"] = 10, ["incomplete"] = 12, ["multi-turn"] = 10,
                ["relative-dates"] = 6, ["faq"] = 10, ["booking"] = 6, ["out-of-scope"] = 5
            });
    }

    [Fact]
    public void Scenarios_ReferenceKnownToolsSlotsAndCars()
    {
        var carIds = Enumerable.Range(1, 6).Concat(DemoFleet.Create().Select(c => c.Id)).ToHashSet();
        var slots = typeof(SlotNames).GetFields().Select(f => (string)f.GetValue(null)!).ToHashSet();
        var now = DateOnly.Parse("2026-11-18");

        foreach (var s in Scenarios.Scenarios)
        {
            s.Turns.Should().NotBeEmpty(s.Id);
            if (s.Expected.Tool is { } tool) Tools.Should().Contain(tool, s.Id);
            s.Expected.OptionalTools.Concat(s.Expected.MustNotCallTools.Where(t => t != "*")).Should().BeSubsetOf(Tools, s.Id);
            s.Expected.AsksFor.Should().BeSubsetOf(slots, s.Id);
            if (s.Expected.Args.Count > 0) s.Expected.Tool.Should().NotBeNull(s.Id);

            foreach (var (key, value) in s.Expected.Args)
            {
                if (key is "pickupDate" or "returnDate")
                {
                    var date = DateOnly.Parse(value.GetString()!);
                    date.Should().BeOnOrAfter(now, s.Id).And.BeOnOrBefore(now.AddDays(61), s.Id);
                }
                if (key == "carId") carIds.Should().Contain(value.GetInt32(), s.Id);
            }
        }
    }

    [Fact]
    public void Qrels_ReferenceExistingChunksAndHaveBalancedSplits()
    {
        var kb = Path.Combine(Root, "BookingSystem", "KnowledgeBase");
        var keys = Directory.GetFiles(kb, "*.md")
            .SelectMany(f => MarkdownChunker.Parse(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f)).Chunks
                .Select(c => $"{Path.GetFileNameWithoutExtension(f)}#{c.Index}"))
            .ToHashSet();
        var queries = Qrels.Queries;

        queries.Should().HaveCountGreaterThanOrEqualTo(50);
        queries.Select(q => q.Id).Should().OnlyHaveUniqueItems();
        queries.SelectMany(q => q.Relevant).Should().OnlyContain(k => keys.Contains(k));
        var devShare = queries.Count(q => q.Split == "dev") / (double)queries.Count;
        devShare.Should().BeInRange(0.25, 0.35);
        foreach (var split in new[] { "dev", "test" })
            queries.Where(q => q.Split == split).Select(q => q.Kind).Distinct().Should().BeEquivalentTo(["keyword", "descriptive"]);
    }
}
