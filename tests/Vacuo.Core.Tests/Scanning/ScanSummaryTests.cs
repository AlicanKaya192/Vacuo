using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Scanning;

public class ScanSummaryTests
{
    private static readonly CacheRule Cache = new KnownLocationRule("cache", Bucket.RebuildsItself, RebuildCost.Redownloaded, KnownRoot.LocalAppData, "c");
    private static readonly CacheRule Kept = new KnownLocationRule("system", Bucket.Keep, RebuildCost.Redownloaded, KnownRoot.LocalAppData, "s");

    private static ScanFinding Finding(string path, CacheRule rule, long bytes, int skipped = 0) =>
        new(path, rule, new DirectorySize(bytes, skipped), DateTime.UnixEpoch);

    [Fact]
    public void Combine_FindingsOfSeveralScans_AreListedLargestFirst()
    {
        var first = new ScanResult([Finding("a", Cache, 10), Finding("b", Cache, 300)], SkippedEntries: 0);
        var second = new ScanResult([Finding("c", Cache, 50)], SkippedEntries: 0);

        ScanSummary summary = ScanSummary.Combine([first, second]);

        Assert.Equal(["b", "c", "a"], summary.Findings.Select(finding => finding.Path));
    }

    [Fact]
    public void Combine_TotalsEachBucketAndAll()
    {
        var result = new ScanResult([Finding("a", Cache, 10), Finding("b", Kept, 300), Finding("c", Cache, 50)], SkippedEntries: 0);

        ScanSummary summary = ScanSummary.Combine([result]);

        Assert.Equal([new BucketTotal(Bucket.RebuildsItself, 60, 2), new BucketTotal(Bucket.Keep, 300, 1)], summary.BucketTotals);
        Assert.Equal(360, summary.TotalBytes);
    }

    [Fact]
    public void Combine_CountsSkippedFoldersFromTheSearchAndFromInsideFindings()
    {
        var first = new ScanResult([Finding("a", Cache, 10, skipped: 2)], SkippedEntries: 1);
        var second = new ScanResult([], SkippedEntries: 4);

        Assert.Equal(7, ScanSummary.Combine([first, second]).SkippedEntries);
    }

    [Fact]
    public void Combine_NothingFound_IsEmptyWithZeroTotal()
    {
        ScanSummary summary = ScanSummary.Combine([new ScanResult([], SkippedEntries: 0)]);

        Assert.Empty(summary.Findings);
        Assert.Empty(summary.BucketTotals);
        Assert.Equal(0, summary.TotalBytes);
    }

    [Fact]
    public void Without_DropsTheGivenFindingsAndRecountsTotals()
    {
        ScanSummary summary = ScanSummary.Combine(
            [new ScanResult([Finding("a", Cache, 10), Finding("b", Kept, 300), Finding("c", Cache, 50)], SkippedEntries: 0)]);

        ScanSummary rest = summary.Without(["c"]);

        Assert.Equal(["b", "a"], rest.Findings.Select(finding => finding.Path));
        Assert.Equal([new BucketTotal(Bucket.RebuildsItself, 10, 1), new BucketTotal(Bucket.Keep, 300, 1)], rest.BucketTotals);
        Assert.Equal(310, rest.TotalBytes);
    }

    [Fact]
    public void Without_LastFindingOfABucket_RemovesTheBucketTotal()
    {
        ScanSummary summary = ScanSummary.Combine([new ScanResult([Finding("a", Cache, 10), Finding("b", Kept, 300)], SkippedEntries: 0)]);

        Assert.Equal([new BucketTotal(Bucket.Keep, 300, 1)], summary.Without(["a"]).BucketTotals);
    }

    [Fact]
    public void Without_SubtractsOnlyTheSkippedFoldersInsideRemovedFindings()
    {
        ScanSummary summary = ScanSummary.Combine(
            [new ScanResult([Finding("a", Cache, 10, skipped: 2), Finding("b", Cache, 20, skipped: 1)], SkippedEntries: 4)]);

        Assert.Equal(5, summary.Without(["a"]).SkippedEntries);
    }

    [Fact]
    public void Without_MatchesPathsIgnoringCase()
    {
        ScanSummary summary = ScanSummary.Combine([new ScanResult([Finding(@"C:\Cache\Pip", Cache, 10)], SkippedEntries: 0)]);

        Assert.Empty(summary.Without([@"c:\cache\pip"]).Findings);
    }

    [Fact]
    public void Without_UnknownPath_LeavesTheSummaryAsItWas()
    {
        ScanSummary summary = ScanSummary.Combine([new ScanResult([Finding("a", Cache, 10, skipped: 1)], SkippedEntries: 2)]);

        ScanSummary rest = summary.Without(["z"]);

        Assert.Equal(summary.Findings, rest.Findings);
        Assert.Equal(summary.BucketTotals, rest.BucketTotals);
        Assert.Equal(summary.TotalBytes, rest.TotalBytes);
        Assert.Equal(summary.SkippedEntries, rest.SkippedEntries);
    }
}
