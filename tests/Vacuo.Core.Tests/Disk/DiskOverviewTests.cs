using Vacuo.Core.Disk;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Disk;

public class DiskOverviewTests
{
    private const string SystemDrive = @"C:\";
    private static readonly CacheRule Cache = KnownCacheRules.KnownLocations.Single(rule => rule.Id == "pip-cache");
    private static readonly CacheRule Screenshots = UserFileRules.All.Single(rule => rule.Id == "screenshots");

    private static ScanSummary SummaryOf(params ScanFinding[] findings) =>
        ScanSummary.Combine([new ScanResult(findings, SkippedEntries: 0)]);

    private static ScanFinding Finding(string path, CacheRule rule, long bytes) =>
        new(path, rule, new DirectorySize(bytes, 0), DateTime.UnixEpoch);

    [Fact]
    public void From_EachBucketTakesItsShareOfTheWholeDrive()
    {
        ScanSummary summary = SummaryOf(
            Finding(@"C:\cache\a", Cache, 100),
            Finding(@"C:\cache\b", Cache, 150),
            Finding(@"C:\shots\a.png", Screenshots, 50));

        DiskOverview overview = DiskOverview.From(summary, SystemDrive, freeBytes: 400, totalBytes: 1000);

        Assert.Equal(
            [new DiskSegment(Bucket.RebuildsItself, 250, 0.25), new DiskSegment(Bucket.Yours, 50, 0.05)],
            overview.Segments);
    }

    [Fact]
    public void From_FreeSpaceIsItsShareOfTheDrive()
    {
        DiskOverview overview = DiskOverview.From(SummaryOf(), SystemDrive, freeBytes: 400, totalBytes: 1000);

        Assert.Equal(400, overview.FreeBytes);
        Assert.Equal(0.4, overview.FreeFraction);
        Assert.Empty(overview.Segments);
    }

    [Fact]
    public void From_FindingsOnAnotherDrive_AreLeftOut()
    {
        ScanSummary summary = SummaryOf(Finding(@"C:\cache\a", Cache, 100), Finding(@"D:\cache\b", Cache, 900));

        DiskOverview overview = DiskOverview.From(summary, SystemDrive, freeBytes: 400, totalBytes: 1000);

        Assert.Equal(100, Assert.Single(overview.Segments).Bytes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 1000)]
    [InlineData(1001, 1000)]
    public void From_ImpossibleDriveSizes_Throw(long freeBytes, long totalBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DiskOverview.From(SummaryOf(), SystemDrive, freeBytes, totalBytes));
    }
}
