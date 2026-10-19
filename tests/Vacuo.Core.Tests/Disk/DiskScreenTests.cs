using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;
using Vacuo.Core.Scanning;
using Vacuo.Core.Tests.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Disk;

public class DiskScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly CacheRule Pip = KnownCacheRules.KnownLocations.Single(rule => rule.Id == "pip-cache");
    private static readonly CacheRule NodeModules = KnownCacheRules.ProjectArtifacts.Single(rule => rule.Id == "node-modules");
    private static readonly CacheRule Screenshots = UserFileRules.All.Single(rule => rule.Id == "screenshots");
    private static readonly CacheRule SystemFiles = new KnownLocationRule("system-test", Bucket.Keep, RebuildCost.CannotBeRebuilt, KnownRoot.LocalAppData, "s");

    private sealed class Setup : IDisposable
    {
        public Setup()
        {
            var time = new FixedTimeProvider(Now);
            Bin = new FakeRecycleBin(Tree.PathOf("bin"), time);
            Journal = new ActionJournal(Tree.PathOf("journal.jsonl"));
            Runner = new ActionRunner(Journal, Bin, time);
        }

        public TempTree Tree { get; } = new();
        public FakeRecycleBin Bin { get; }
        public ActionJournal Journal { get; }
        public ActionRunner Runner { get; }

        public ScanFinding Finding(string relativePath, CacheRule rule, int sizeBytes)
        {
            string path = Tree.AddFile(relativePath, sizeBytes);
            return new ScanFinding(path, rule, new DirectorySize(sizeBytes, 0), DateTime.UnixEpoch);
        }

        public void Dispose() => Tree.Dispose();
    }

    private static DiskScreen ScreenOf(params ScanFinding[] findings) =>
        new(ScanSummary.Combine([new ScanResult(findings, SkippedEntries: 0)]));

    private sealed class RefusingRecycleBin(ActionRefusedException refusal) : IRecycleBin
    {
        public RecycledItem MoveToBin(string path) => throw refusal;

        public void Restore(RecycledItem item) => throw new InvalidOperationException("Nothing was recycled.");
    }

    [Fact]
    public void Buckets_GroupFindingsLargestFirstWithTotals()
    {
        using var setup = new Setup();
        ScanFinding pip = setup.Finding(@"pip\entry", Pip, 300);
        ScanFinding node = setup.Finding(@"web\node_modules\a.js", NodeModules, 500);
        ScanFinding shot = setup.Finding(@"Screenshots\a.png", Screenshots, 40);

        DiskScreen screen = ScreenOf(pip, node, shot);

        Assert.Equal([Bucket.RebuildsItself, Bucket.Yours], screen.Buckets.Select(bucket => bucket.Bucket));
        Assert.Equal([node, pip], screen.Buckets[0].Items);
        Assert.Equal(800, screen.Buckets[0].TotalBytes);
        Assert.Equal(40, screen.Buckets[1].TotalBytes);
    }

    [Fact]
    public void NewScreen_HasNothingSelected()
    {
        using var setup = new Setup();
        DiskScreen screen = ScreenOf(setup.Finding(@"pip\entry", Pip, 300), setup.Finding(@"Screenshots\a.png", Screenshots, 40));

        Assert.Equal(0, screen.SelectedCount);
        Assert.Equal(0, screen.SelectedBytes);
        Assert.Empty(screen.Preview());
    }

    [Fact]
    public void SelectAndDeselect_UpdateCountAndBytes()
    {
        using var setup = new Setup();
        ScanFinding pip = setup.Finding(@"pip\entry", Pip, 300);
        ScanFinding shot = setup.Finding(@"Screenshots\a.png", Screenshots, 40);
        DiskScreen screen = ScreenOf(pip, shot);

        screen.Select(pip.Path);
        screen.Select(shot.Path);
        screen.Deselect(shot.Path);

        Assert.True(screen.IsSelected(pip.Path));
        Assert.False(screen.IsSelected(shot.Path));
        Assert.Equal(1, screen.SelectedCount);
        Assert.Equal(300, screen.SelectedBytes);
    }

    [Fact]
    public void Select_ItemInTheKeepBucket_IsRefused()
    {
        using var setup = new Setup();
        ScanFinding system = setup.Finding(@"system\file", SystemFiles, 10);
        DiskScreen screen = ScreenOf(system);

        Assert.False(screen.IsSelectable(system.Path));
        Assert.Throws<InvalidOperationException>(() => screen.Select(system.Path));
        Assert.Equal(0, screen.SelectedCount);
    }

    [Fact]
    public void Select_PathNotOnTheScreen_IsRefused()
    {
        using var setup = new Setup();
        DiskScreen screen = ScreenOf(setup.Finding(@"pip\entry", Pip, 300));

        Assert.Throws<ArgumentException>(() => screen.Select(setup.Tree.PathOf("elsewhere.txt")));
    }

    [Fact]
    public void Preview_ListsTheSelectedItemsLargestFirst()
    {
        using var setup = new Setup();
        ScanFinding pip = setup.Finding(@"pip\entry", Pip, 300);
        ScanFinding node = setup.Finding(@"web\node_modules\a.js", NodeModules, 500);
        ScanFinding shot = setup.Finding(@"Screenshots\a.png", Screenshots, 40);
        DiskScreen screen = ScreenOf(pip, node, shot);
        screen.Select(shot.Path);
        screen.Select(node.Path);

        Assert.Equal([node, shot], screen.Preview());
    }

    [Fact]
    public void MoveSelected_MovesOnlyTheSelectedItemsAndReportsWhatWasFreed()
    {
        using var setup = new Setup();
        ScanFinding pip = setup.Finding(@"pip\entry", Pip, 300);
        ScanFinding shot = setup.Finding(@"Screenshots\a.png", Screenshots, 40);
        DiskScreen screen = ScreenOf(pip, shot);
        screen.Select(pip.Path);

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.False(File.Exists(pip.Path));
        Assert.True(File.Exists(shot.Path));
        Assert.Equal(300, outcome.FreedBytes);
        Assert.Empty(outcome.Failed);
        Assert.Equal(outcome.Moved, setup.Journal.ReadAll());
        Assert.Equal([shot], screen.Buckets.SelectMany(bucket => bucket.Items));
        Assert.Equal(0, screen.SelectedCount);
    }

    [Fact]
    public void MoveSelected_OneItemInUse_OthersStillMoveAndTheBusyOneIsReported()
    {
        using var setup = new Setup();
        ScanFinding pip = setup.Finding(@"pip\entry", Pip, 300);
        ScanFinding shot = setup.Finding(@"Screenshots\a.png", Screenshots, 40);
        DiskScreen screen = ScreenOf(pip, shot);
        screen.Select(pip.Path);
        screen.Select(shot.Path);
        // The larger item goes first, so a failure there must not stop the smaller one.
        using FileStream pipRunning = OpenFileProbeTests.OpenLikeAnotherProgram(pip.Path);

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.True(File.Exists(pip.Path));
        Assert.False(File.Exists(shot.Path));
        Assert.Equal([new MoveFailure(pip.Path, MoveFailureReason.InUse)], outcome.Failed);
        Assert.True(screen.IsSelected(pip.Path));
    }

    [Fact]
    public void MoveSelected_ItemGoneSinceTheScan_IsReportedMissing()
    {
        using var setup = new Setup();
        ScanFinding pip = setup.Finding(@"pip\entry", Pip, 300);
        DiskScreen screen = ScreenOf(pip);
        screen.Select(pip.Path);
        File.Move(pip.Path, setup.Tree.PathOf("moved-by-user"));

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Equal([new MoveFailure(pip.Path, MoveFailureReason.Missing)], outcome.Failed);
        Assert.Equal(0, setup.Bin.ItemCount);
    }

    [Fact]
    public void MoveSelected_ItemTheBinCannotTake_IsReportedNotRecyclable()
    {
        using var setup = new Setup();
        ScanFinding pip = setup.Finding(@"pip\entry", Pip, 300);
        DiskScreen screen = ScreenOf(pip);
        screen.Select(pip.Path);
        var bin = new RefusingRecycleBin(new NotRecyclableException("too large"));

        MoveOutcome outcome = screen.MoveSelected(new ActionRunner(setup.Journal, bin, new FixedTimeProvider(Now)), bin);

        Assert.Equal([new MoveFailure(pip.Path, MoveFailureReason.NotRecyclable)], outcome.Failed);
        Assert.True(File.Exists(pip.Path));
    }

    [Fact]
    public void MoveSelected_AccessDenied_IsReportedNoAccess()
    {
        using var setup = new Setup();
        ScanFinding pip = setup.Finding(@"pip\entry", Pip, 300);
        DiskScreen screen = ScreenOf(pip);
        screen.Select(pip.Path);
        var bin = new RefusingRecycleBin(new ActionRefusedException("denied", new UnauthorizedAccessException()));

        MoveOutcome outcome = screen.MoveSelected(new ActionRunner(setup.Journal, bin, new FixedTimeProvider(Now)), bin);

        Assert.Equal([new MoveFailure(pip.Path, MoveFailureReason.NoAccess)], outcome.Failed);
    }

    [Fact]
    public void MoveSelected_NothingSelected_DoesNothing()
    {
        using var setup = new Setup();
        ScanFinding pip = setup.Finding(@"pip\entry", Pip, 300);
        DiskScreen screen = ScreenOf(pip);

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Empty(outcome.Moved);
        Assert.Empty(outcome.Failed);
        Assert.True(File.Exists(pip.Path));
    }
}
