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
}
