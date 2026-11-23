using BookingSystem.Ai.Knowledge;
using FluentAssertions;

namespace BookingSystem.Tests.Ai.Knowledge;

public class Bm25IndexTests
{
    private static Bm25Index Index(params (int, string)[] docs) => new(docs);

    [Fact]
    public void MatchesTheClosedFormForASingleTerm()
    {
        // N = 3, df("депозит") = 1, |D1| = 2, avgdl = 2, tf = 1 -> norm = 1
        var index = Index((1, "депозит картка"), (2, "франшиза страхування"), (3, "пальне бак"));

        var hit = index.Search("депозит", 5).Single();

        var idf = Math.Log(1 + (3 - 1 + 0.5) / (1 + 0.5));
        var expected = idf * 1 * (Bm25Index.DefaultK1 + 1) / (1 + Bm25Index.DefaultK1);
        hit.Id.Should().Be(1);
        hit.Score.Should().BeApproximately(expected, 1e-12);
    }

    [Fact]
    public void RareTermsOutweighFrequentOnes()
    {
        var index = Index((1, "оплата франшиза"), (2, "оплата картка"), (3, "оплата готівка"));

        index.Search("оплата франшиза", 3).First().Id.Should().Be(1);
    }

    [Fact]
    public void TermFrequencySaturatesAndLongDocumentsArePenalised()
    {
        var index = Index((1, "ліміт ліміт ліміт ліміт ліміт а б в г д е є ж"), (2, "ліміт а"), (3, "інше слово"));

        var scores = index.Search("ліміт", 3).ToDictionary(x => x.Id, x => x.Score);

        scores[2].Should().BeGreaterThan(scores[1] * 0.5);   // saturation: 5 repeats are far from 5x
        scores[1].Should().BeLessThan(scores[2] * 5);
        scores.Should().NotContainKey(3);
    }

    [Fact]
    public void RepeatedQueryTermsCountOnceAndTiesGoToTheLowerId()
    {
        var index = Index((2, "залог"), (1, "залог"));

        index.Search("залог залог", 2).Select(x => x.Id).Should().Equal(1, 2);
        index.Search("залог залог", 1).Single().Score.Should().Be(index.Search("залог", 1).Single().Score);
    }

    [Fact]
    public void UnknownTermsAndEmptyInputGiveNothing()
    {
        var index = Index((1, "депозит"));

        index.Search("невідоме", 3).Should().BeEmpty();
        index.Search("  ", 3).Should().BeEmpty();
        Index().Search("депозит", 3).Should().BeEmpty();
    }

    [Fact]
    public void ExpansionAddsFeedbackTermsWithLowerWeight()
    {
        var index = Index((1, "депозит блокування картка"), (2, "франшиза страхування"), (3, "пальне бак"));

        var terms = index.Expand("заблоку гроші", [1], terms: 2, weight: 0.5);

        terms["заблоку"].Should().Be(1);
        terms["гроші"].Should().Be(1);
        terms.Keys.Except(["заблоку", "гроші"]).Should().HaveCount(2).And.BeSubsetOf(["депозит", "блокування", "картка"]);
        terms.Where(t => t.Value < 1).Should().OnlyContain(t => t.Value > 0 && t.Value <= 0.5);
        terms.Values.Max(v => v < 1 ? v : 0).Should().Be(0.5);
    }

    [Fact]
    public void ExpandedQueryFindsDocumentWithoutSharedWords()
    {
        var index = Index((1, "депозит блокування картка"), (2, "франшиза страхування"), (3, "пальне бак"));

        index.Search("заблоку", 3).Should().BeEmpty();
        index.Search(index.Expand("заблоку", [1], 10, 0.5), 3).First().Id.Should().Be(1);
    }

    [Fact]
    public void ExpansionWithoutFeedbackKeepsTheQuery()
    {
        var index = Index((1, "депозит"));

        index.Expand("депозит", [], 10, 0.5).Should().BeEquivalentTo(new Dictionary<string, double> { ["депозит"] = 1 });
        index.Expand("депозит", [42], 10, 0.5).Should().ContainSingle();
    }

    [Fact]
    public void LimitIsRespected()
    {
        var index = Index((1, "а"), (2, "а б"), (3, "а в"), (4, "а г"));

        index.Search("а", 2).Should().HaveCount(2);
    }
}
