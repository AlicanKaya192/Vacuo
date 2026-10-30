using Vacuo.Core.Disk;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Disk;

public class DiskHistoryTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);

    private static DiskSnapshot Snapshot(DateOnly day, long usedBytes = 100) => new(day, usedBytes, []);

    [Fact]
    public void Recording_KeepsTheSnapshot()
    {
        DiskHistory history = DiskHistory.Empty.Recorded(Snapshot(Today));

        Assert.Equal(Today, Assert.Single(history.Snapshots).Day);
    }

    [Fact]
    public void Recording_TwiceInADay_KeepsOnlyTheLatest()
    {
        DiskHistory history = DiskHistory.Empty
            .Recorded(Snapshot(Today, usedBytes: 100))
            .Recorded(Snapshot(Today, usedBytes: 200));

        Assert.Equal(200, Assert.Single(history.Snapshots).UsedBytes);
    }

    [Fact]
    public void Recording_DropsWhatIsOlderThanThirtyDays()
    {
        DateOnly kept = Today.AddDays(-DiskHistory.DaysKept);
        DateOnly dropped = kept.AddDays(-1);

        DiskHistory history = DiskHistory.Empty
            .Recorded(Snapshot(dropped))
            .Recorded(Snapshot(kept))
            .Recorded(Snapshot(Today));

        Assert.Equal([kept, Today], history.Snapshots.Select(snapshot => snapshot.Day));
    }

    // The clock was set back: a snapshot "from the future" would never age out.
    [Fact]
    public void Recording_DropsDaysAfterTheNewOne()
    {
        DiskHistory history = DiskHistory.Empty
            .Recorded(Snapshot(Today.AddDays(5)))
            .Recorded(Snapshot(Today));

        Assert.Equal(Today, Assert.Single(history.Snapshots).Day);
    }

    [Fact]
    public void Snapshot_TakesEachMeasuredFoldersBytes()
    {
        var root = new CategoryRoot(DiskCategory.DocumentsAndMedia, @"C:\Users\Someone\Downloads");
        CategorySize[] sizes = [new CategorySize(root, new DirectorySize(700, 0))];

        DiskSnapshot snapshot = DiskSnapshot.From(Today, 5_000, sizes);

        Assert.Equal(5_000, snapshot.UsedBytes);
        FolderBytes folder = Assert.Single(snapshot.Folders);
        Assert.Equal(root.Path, folder.Path);
        Assert.Equal(700, folder.Bytes);
    }
}
