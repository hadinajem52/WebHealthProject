using FluentAssertions;
using WebHealth.Domain.Crawling;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// BR-L07. A target is fetched once but may be linked from many pages, and the source page is what
/// makes the report actionable (AC-08). Order of discovery and outcome must not change the result.
/// </summary>
public sealed class CrawlLinkLedgerTests
{
    private static CrawlRequestObservation Ok => new(CrawlRequestOutcome.Responded, 200, 0);

    private static CrawlRequestObservation NotFound => new(CrawlRequestOutcome.Responded, 404, 0);

    [Fact]
    public void RecordDiscovery_EmitsNothingUntilTheTargetResolves()
    {
        var ledger = new CrawlLinkLedger();

        ledger.RecordDiscovery("https://a.test/source", "https://a.test/target").Should().BeEmpty();

        var edges = ledger.RecordOutcome("https://a.test/target", NotFound);

        edges.Should().ContainSingle();
        edges[0].SourceUrl.Should().Be("https://a.test/source");
        edges[0].Classification.Should().Be(CrawlLinkClassifications.Broken);
        edges[0].StatusCode.Should().Be(404);
    }

    [Fact]
    public void RecordDiscovery_EmitsImmediatelyWhenTheTargetIsAlreadyResolved()
    {
        var ledger = new CrawlLinkLedger();
        ledger.RecordOutcome("https://a.test/target", NotFound);

        var edges = ledger.RecordDiscovery("https://a.test/late", "https://a.test/target");

        edges.Should().ContainSingle().Which.SourceUrl.Should().Be("https://a.test/late");
    }

    [Fact]
    public void RecordOutcome_EmitsOneEdgePerSourceThatPointsAtTheTarget()
    {
        var ledger = new CrawlLinkLedger();
        ledger.RecordDiscovery("https://a.test/one", "https://a.test/target");
        ledger.RecordDiscovery("https://a.test/two", "https://a.test/target");

        var edges = ledger.RecordOutcome("https://a.test/target", NotFound);

        edges.Select(edge => edge.SourceUrl).Should()
            .BeEquivalentTo(["https://a.test/one", "https://a.test/two"]);
    }

    [Fact]
    public void Ledger_DeduplicatesRepeatedLinksFromOneSource()
    {
        var ledger = new CrawlLinkLedger();
        for (var index = 0; index < 5; index++)
        {
            ledger.RecordDiscovery("https://a.test/one", "https://a.test/target");
        }

        var edges = ledger.RecordOutcome("https://a.test/target", NotFound);

        edges.Should().ContainSingle("five identical links are one affected page, not five");
    }

    [Fact]
    public void Ledger_RecordsASeedAsAnEdgeWithNoSource()
    {
        var ledger = new CrawlLinkLedger();
        ledger.RecordDiscovery(null, "https://a.test/");

        var edges = ledger.RecordOutcome("https://a.test/", Ok);

        edges.Should().ContainSingle().Which.SourceUrl.Should().BeNull();
    }

    [Fact]
    public void RecordSkip_ResolvesTheTargetWithItsReason()
    {
        var ledger = new CrawlLinkLedger();
        ledger.RecordDiscovery("https://a.test/source", "https://other.test/x");

        var edges = ledger.RecordSkip("https://other.test/x", CrawlSkipReasons.TargetNotAuthorized);

        edges.Should().ContainSingle();
        edges[0].Classification.Should().Be(CrawlLinkClassifications.Skipped);
        edges[0].SkipReason.Should().Be(CrawlSkipReasons.TargetNotAuthorized);
    }

    [Fact]
    public void Ledger_KeepsTheFirstResolutionOfATarget()
    {
        var ledger = new CrawlLinkLedger();
        ledger.RecordDiscovery("https://a.test/source", "https://a.test/target");
        ledger.RecordOutcome("https://a.test/target", NotFound);

        ledger.RecordOutcome("https://a.test/target", Ok).Should()
            .BeEmpty("a late second outcome must not rewrite a result already handed to the sink");
    }

    [Fact]
    public void Flush_ReportsADiscoveredTargetThatWasNeverReached()
    {
        var ledger = new CrawlLinkLedger();
        ledger.RecordDiscovery("https://a.test/source", "https://a.test/reached");
        ledger.RecordDiscovery("https://a.test/source", "https://a.test/unreached");
        ledger.RecordOutcome("https://a.test/reached", Ok);

        var edges = ledger.Flush();

        edges.Should().ContainSingle();
        edges[0].TargetUrl.Should().Be("https://a.test/unreached");
        edges[0].Classification.Should().Be(CrawlLinkClassifications.Unknown,
            "a target the run never reached must never look healthy");
        edges[0].SkipReason.Should().Be(CrawlSkipReasons.RunStopped);
    }

    [Fact]
    public void Flush_EmitsNothingWhenEveryTargetResolved()
    {
        var ledger = new CrawlLinkLedger();
        ledger.RecordDiscovery(null, "https://a.test/");
        ledger.RecordOutcome("https://a.test/", Ok);

        ledger.Flush().Should().BeEmpty();
    }

    [Fact]
    public void Ledger_ProducesTheSameEdgesWhateverTheOrderOfDiscoveryAndOutcome()
    {
        var discoveryFirst = new CrawlLinkLedger();
        var edges = new List<CrawlEdge>();
        edges.AddRange(discoveryFirst.RecordDiscovery("https://a.test/one", "https://a.test/t"));
        edges.AddRange(discoveryFirst.RecordOutcome("https://a.test/t", NotFound));
        edges.AddRange(discoveryFirst.RecordDiscovery("https://a.test/two", "https://a.test/t"));

        var outcomeFirst = new CrawlLinkLedger();
        var reversed = new List<CrawlEdge>();
        reversed.AddRange(outcomeFirst.RecordDiscovery("https://a.test/one", "https://a.test/t"));
        reversed.AddRange(outcomeFirst.RecordDiscovery("https://a.test/two", "https://a.test/t"));
        reversed.AddRange(outcomeFirst.RecordOutcome("https://a.test/t", NotFound));

        edges.Should().BeEquivalentTo(reversed);
    }
}
