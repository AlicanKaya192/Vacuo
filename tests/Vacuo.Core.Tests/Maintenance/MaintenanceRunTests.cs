using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;
using Vacuo.Core.Maintenance;
using Vacuo.Core.Scanning;
using Vacuo.Core.Tests.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Maintenance;

public class MaintenanceRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Items_GoToTheRecycleBin_AsOneMaintenanceBatch()
    {
        using var tree = new TempTree();
        var (run, journal, bin) = Setup(tree);
        ScanFinding[] items = [Item(tree, @"Temp\a", 100), Item(tree, @"Temp\b", 200)];

        MaintenanceOutcome outcome = run.Run(items, shouldStop: () => false);

        Assert.Equal(2, outcome.Moved.Count);
        Assert.Equal(300, outcome.MovedBytes);
        Assert.False(outcome.Stopped);
        Assert.Equal(2, bin.ItemCount);
        IReadOnlyList<JournalEntry> logged = journal.ReadAll();
        Assert.All(logged, entry => Assert.Equal(JournalBatchKind.Maintenance, entry.BatchKind));
        Assert.Single(logged.Select(entry => entry.BatchId).Distinct());
    }

    [Fact]
    public void UserComingBack_StopsBeforeTheNextItem()
    {
        using var tree = new TempTree();
        var (run, _, bin) = Setup(tree);
        ScanFinding[] items = [Item(tree, @"Temp\a", 100), Item(tree, @"Temp\b", 200)];
        int asked = 0;

        MaintenanceOutcome outcome = run.Run(items, shouldStop: () => asked++ > 0);

        Assert.True(outcome.Stopped);
        Assert.Single(outcome.Moved);
        Assert.Equal(1, bin.ItemCount);
        Assert.True(Directory.Exists(tree.PathOf(@"Temp\b")));
    }

    [Fact]
    public void ItemGoneSinceTheScan_IsAFailure_AndTheRestGoesOn()
    {
        using var tree = new TempTree();
        var (run, _, _) = Setup(tree);
        ScanFinding gone = Item(tree, @"Temp\gone", 100);
        Directory.Delete(gone.Path, recursive: true);

        MaintenanceOutcome outcome = run.Run([gone, Item(tree, @"Temp\b", 200)], shouldStop: () => false);

        Assert.Single(outcome.Moved);
        Assert.Equal(new MoveFailure(gone.Path, MoveFailureReason.Missing), Assert.Single(outcome.Failed));
    }

    [Fact]
    public void Summary_SaysWhatWasDone()
    {
        using var tree = new TempTree();
        var (run, _, _) = Setup(tree);
        ScanFinding gone = Item(tree, @"Temp\gone", 100);
        Directory.Delete(gone.Path, recursive: true);

        MaintenanceSummary summary = run.Run([gone, Item(tree, @"Temp\b", 200)], shouldStop: () => false).Summary;

        Assert.Equal(new MaintenanceSummary(Moved: 1, Bytes: 200, Failed: 1, Stopped: false), summary);
    }

    private static (MaintenanceRun Run, ActionJournal Journal, FakeRecycleBin Bin) Setup(TempTree tree)
    {
        var time = new FixedTimeProvider(Now);
        var journal = new ActionJournal(tree.PathOf("journal.jsonl"));
        var bin = new FakeRecycleBin(tree.PathOf("bin"), time);
        return (new MaintenanceRun(new ActionRunner(journal, bin, time), bin), journal, bin);
    }

    private static ScanFinding Item(TempTree tree, string relativePath, int size)
    {
        tree.AddFile(relativePath + @"\data.tmp", size);
        return new ScanFinding(tree.PathOf(relativePath), UserTempScanner.Rule, new DirectorySize(size, 0), Now.UtcDateTime.AddDays(-30));
    }
}
