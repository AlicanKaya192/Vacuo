using Vacuo.Core.Disk;

namespace Vacuo.Core.Tests.Disk;

public class DiskGrowthTests
{
    private const string Downloads = @"C:\Users\Someone\Downloads";
    private const string Videos = @"C:\Users\Someone\Videos";
    private const string Music = @"C:\Users\Someone\Music";
    private const string Pictures = @"C:\Users\Someone\Pictures";

    private static readonly DateOnly Today = new(2026, 10, 30);

    private static DiskSnapshot Snapshot(DateOnly day, long usedBytes, params (string Path, long Bytes)[] folders) =>
        new(day, usedBytes, folders.Select(folder => new FolderBytes(folder.Path, folder.Bytes)).ToList());

    private static DiskHistory History(params DiskSnapshot[] snapshots) => new(snapshots);

    [Fact]
    public void NoHistory_HasNoGrowth()
    {
        Assert.Null(DiskGrowth.From(DiskHistory.Empty, Today));
    }

    [Fact]
    public void OneDayOfHistory_HasNoGrowth()
    {
        Assert.Null(DiskGrowth.From(History(Snapshot(Today, 100)), Today));
    }

    [Fact]
    public void ComparesTheOldestDayWithTheLatest()
    {
        DiskHistory history = History(
            Snapshot(Today.AddDays(-12), 1_000),
            Snapshot(Today.AddDays(-5), 9_000),
            Snapshot(Today, 1_500));

        DiskGrowth growth = DiskGrowth.From(history, Today)!;

        Assert.Equal(12, growth.Days);
        Assert.Equal(500, growth.ChangeBytes);
    }

    [Fact]
    public void AnEmptierDrive_IsANegativeChange()
    {
        DiskHistory history = History(Snapshot(Today.AddDays(-1), 1_000), Snapshot(Today, 400));

        Assert.Equal(-600, DiskGrowth.From(history, Today)!.ChangeBytes);
    }

    [Fact]
    public void Folders_AreTheThreeThatGrewMost_LargestFirst()
    {
        DiskHistory history = History(
            Snapshot(Today.AddDays(-3), 0, (Downloads, 100), (Videos, 100), (Music, 100), (Pictures, 100)),
            Snapshot(Today, 0, (Downloads, 200), (Videos, 500), (Music, 150), (Pictures, 120)));

        DiskGrowth growth = DiskGrowth.From(history, Today)!;

        Assert.Equal(
            [new FolderGrowth(Videos, 400), new FolderGrowth(Downloads, 100), new FolderGrowth(Music, 50)],
            growth.Folders);
    }

    [Fact]
    public void Folders_LeaveOutThoseThatDidNotGrow()
    {
        DiskHistory history = History(
            Snapshot(Today.AddDays(-3), 0, (Downloads, 500), (Videos, 100)),
            Snapshot(Today, 0, (Downloads, 200), (Videos, 100)));

        Assert.Empty(DiskGrowth.From(history, Today)!.Folders);
    }

    [Fact]
    public void AFolderNewSinceTheOldestDay_GrewFromNothing()
    {
        DiskHistory history = History(
            Snapshot(Today.AddDays(-3), 0),
            Snapshot(Today, 0, (Downloads, 300)));

        Assert.Equal(new FolderGrowth(Downloads, 300), Assert.Single(DiskGrowth.From(history, Today)!.Folders));
    }

    // Windows paths ignore case.
    [Fact]
    public void Folders_MatchRegardlessOfCase()
    {
        DiskHistory history = History(
            Snapshot(Today.AddDays(-3), 0, (Downloads.ToUpperInvariant(), 100)),
            Snapshot(Today, 0, (Downloads, 150)));

        Assert.Equal(50, Assert.Single(DiskGrowth.From(history, Today)!.Folders).GrownBytes);
    }

    [Fact]
    public void DaysOutsideTheWindow_DoNotCount()
    {
        DiskHistory history = History(
            Snapshot(Today.AddDays(-DiskHistory.DaysKept - 1), 1),
            Snapshot(Today.AddDays(-2), 1_000),
            Snapshot(Today, 1_200),
            Snapshot(Today.AddDays(3), 9_999));

        DiskGrowth growth = DiskGrowth.From(history, Today)!;

        Assert.Equal(2, growth.Days);
        Assert.Equal(200, growth.ChangeBytes);
    }

    // A notice must not name a folder from a history abandoned months ago.
    [Fact]
    public void AHistoryLongAbandoned_HasNoGrowth()
    {
        DiskHistory history = History(
            Snapshot(Today.AddDays(-200), 1_000),
            Snapshot(Today.AddDays(-190), 1_200));

        Assert.Null(DiskGrowth.From(history, Today));
    }
}
