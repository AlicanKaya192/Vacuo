using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.DiskMap;
using Vacuo.Core.Journal;
using Vacuo.Core.Tests.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.DiskMap;

public class DiskMapScreenTests
{
    private const long Kept = 100;
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

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

        public string Data => Tree.PathOf("data");

        /// <summary>The tree under "data"; "data\Locked" and its contents cannot be moved.</summary>
        public DiskMapScreen Screen(long usedBytes = 0)
        {
            var protection = new MapProtection(new MapProtectedPaths(
                Data, System: [], Programs: [Path.Join(Data, "Locked")], KeepFolders: [Path.Join(Data, "Keep")]));
            return new DiskMapScreen(new FolderTreeScanner(Kept).Scan(Data, usedBytes, cancellation: TestContext.Current.CancellationToken), protection);
        }

        public string Add(string relative, int bytes) => Tree.AddFile(Path.Join("data", relative), bytes);

        public void Dispose() => Tree.Dispose();
    }

    private sealed class RefusingRecycleBin(ActionRefusedException refusal) : IRecycleBin
    {
        public RecycledItem MoveToBin(string path) => throw refusal;

        public void Restore(RecycledItem item) => throw new InvalidOperationException("Nothing was recycled.");
    }

    private static FolderNode Child(FolderNode node, string name) => node.Children.Single(child => child.Name == name);

    [Fact]
    public void Starts_AtTheRoot_WithNothingSelected()
    {
        using var setup = new Setup();
        setup.Add(@"Keep\a.bin", 300);

        DiskMapScreen screen = setup.Screen();

        Assert.Same(screen.Root, screen.Current);
        Assert.Equal(0, screen.SelectedCount);
        Assert.Equal([screen.Root], screen.Trail);
    }

    [Fact]
    public void Enter_And_Up_MoveAlongTheTrail()
    {
        using var setup = new Setup();
        setup.Add(@"Keep\Deep\a.bin", 300);
        DiskMapScreen screen = setup.Screen();
        FolderNode keep = Child(screen.Root, "Keep");
        FolderNode deep = Child(keep, "Deep");

        screen.Enter(keep);
        screen.Enter(deep);
        Assert.Equal([screen.Root, keep, deep], screen.Trail);

        screen.Up();
        Assert.Same(keep, screen.Current);
    }

    [Fact]
    public void Up_AtTheRoot_StaysThere()
    {
        using var setup = new Setup();
        setup.Add("a.bin", 300);
        DiskMapScreen screen = setup.Screen();

        screen.Up();

        Assert.Same(screen.Root, screen.Current);
    }

    [Fact]
    public void GoTo_JumpsBackAlongTheTrail()
    {
        using var setup = new Setup();
        setup.Add(@"Keep\Deep\a.bin", 300);
        DiskMapScreen screen = setup.Screen();
        FolderNode keep = Child(screen.Root, "Keep");
        screen.Enter(keep);
        screen.Enter(Child(keep, "Deep"));

        screen.GoTo(screen.Root);

        Assert.Equal([screen.Root], screen.Trail);
    }

    [Fact]
    public void AFileOrAFolderWithNothingToShow_CannotBeEntered()
    {
        using var setup = new Setup();
        setup.Add("big.iso", 300);
        setup.Add(@"Flat\a.txt", 60);
        setup.Add(@"Flat\b.txt", 60);
        DiskMapScreen screen = setup.Screen();

        Assert.False(screen.CanEnter(Child(screen.Root, "big.iso")));
        Assert.False(screen.CanEnter(Child(screen.Root, "Flat")));
    }

    [Fact]
    public void AProtectedNode_CannotBeSelected()
    {
        using var setup = new Setup();
        setup.Add(@"Locked\a.bin", 300);
        DiskMapScreen screen = setup.Screen();
        FolderNode locked = Child(screen.Root, "Locked");

        Assert.Equal(MapLock.Programs, screen.LockOf(locked));
        Assert.False(screen.TrySelect(locked));
        Assert.Equal(0, screen.SelectedCount);
    }

    [Fact]
    public void Selecting_AddsUpTheBytes()
    {
        using var setup = new Setup();
        setup.Add(@"A\a.bin", 300);
        setup.Add(@"B\b.bin", 200);
        DiskMapScreen screen = setup.Screen();

        screen.TrySelect(Child(screen.Root, "A"));
        screen.TrySelect(Child(screen.Root, "B"));

        Assert.Equal(2, screen.SelectedCount);
        Assert.Equal(500, screen.SelectedBytes);
        Assert.Equal(["A", "B"], screen.Preview().Select(node => node.Name));
    }

    [Fact]
    public void Unselect_TakesItBackOut()
    {
        using var setup = new Setup();
        setup.Add(@"A\a.bin", 300);
        DiskMapScreen screen = setup.Screen();
        FolderNode a = Child(screen.Root, "A");
        screen.TrySelect(a);

        screen.Unselect(a);

        Assert.False(screen.IsSelected(a));
        Assert.Equal(0, screen.SelectedBytes);
    }

    // Counting a folder and something inside it would show more than a move can free.
    [Fact]
    public void SelectingAFolder_CoversWhatIsInsideIt()
    {
        using var setup = new Setup();
        setup.Add(@"A\Inner\a.bin", 300);
        setup.Add(@"A\b.bin", 200);
        DiskMapScreen screen = setup.Screen();
        FolderNode a = Child(screen.Root, "A");
        FolderNode inner = Child(a, "Inner");

        screen.TrySelect(inner);
        screen.TrySelect(a);

        Assert.Equal(1, screen.SelectedCount);
        Assert.Equal(500, screen.SelectedBytes);
        Assert.True(screen.IsSelected(inner));
        Assert.False(screen.TrySelect(inner));
    }

    [Fact]
    public void Move_SendsTheSelectionToTheBin_AsOneBatch()
    {
        using var setup = new Setup();
        string a = setup.Add(@"A\a.bin", 300);
        string b = setup.Add("b.bin", 200);
        DiskMapScreen screen = setup.Screen();
        screen.TrySelect(Child(screen.Root, "A"));
        screen.TrySelect(Child(screen.Root, "b.bin"));

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Equal(2, outcome.Moved.Count);
        Assert.Empty(outcome.Failed);
        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));
        Assert.Single(outcome.Moved.Select(entry => entry.BatchId).Distinct());
        Assert.All(outcome.Moved, entry => Assert.Equal(JournalBatchKind.DiskMap, entry.BatchKind));
    }

    [Fact]
    public void Move_TakesTheNodesOffTheMap_AndTheirBytesOffEveryParent()
    {
        using var setup = new Setup();
        setup.Add(@"A\Inner\a.bin", 300);
        setup.Add(@"A\b.bin", 200);
        DiskMapScreen screen = setup.Screen();
        FolderNode a = Child(screen.Root, "A");
        screen.TrySelect(Child(a, "Inner"));

        screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.DoesNotContain(a.Children, child => child.Name == "Inner");
        Assert.Equal(200, a.Bytes);
        Assert.Equal(200, screen.Root.Bytes);
        Assert.Equal(0, screen.SelectedCount);
    }

    [Fact]
    public void Move_OfTheFolderBeingViewed_GoesBackToWhatIsLeft()
    {
        using var setup = new Setup();
        setup.Add(@"A\Inner\a.bin", 300);
        setup.Add(@"B\b.bin", 300);
        DiskMapScreen screen = setup.Screen();
        FolderNode a = Child(screen.Root, "A");
        screen.TrySelect(a);
        screen.Enter(a);

        screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Same(screen.Root, screen.Current);
    }

    [Fact]
    public void Move_OfSomethingGoneSinceTheScan_IsAFailure_AndTheRestStillMoves()
    {
        using var setup = new Setup();
        string gone = setup.Add("gone.bin", 300);
        setup.Add("stays.bin", 200);
        DiskMapScreen screen = setup.Screen();
        screen.TrySelect(Child(screen.Root, "gone.bin"));
        screen.TrySelect(Child(screen.Root, "stays.bin"));
        File.Delete(gone);

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Single(outcome.Moved);
        Assert.Equal(MoveFailureReason.Missing, Assert.Single(outcome.Failed).Reason);
    }

    // Items that failed stay selected, so the user can try them again.
    [Fact]
    public void Move_ThatFails_LeavesTheItemSelected()
    {
        using var setup = new Setup();
        string gone = setup.Add("gone.bin", 300);
        DiskMapScreen screen = setup.Screen();
        screen.TrySelect(Child(screen.Root, "gone.bin"));
        File.Delete(gone);

        screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Equal(1, screen.SelectedCount);
    }

    [Fact]
    public void Move_TheBinRefuses_StaysOnTheMapWithItsBytes()
    {
        using var setup = new Setup();
        string a = setup.Add(@"A\a.bin", 300);
        DiskMapScreen screen = setup.Screen();
        FolderNode node = Child(screen.Root, "A");
        screen.TrySelect(node);
        var bin = new RefusingRecycleBin(new NotRecyclableException("too large"));

        MoveOutcome outcome = screen.MoveSelected(new ActionRunner(setup.Journal, bin, new FixedTimeProvider(Now)), bin);

        Assert.Equal(MoveFailureReason.NotRecyclable, Assert.Single(outcome.Failed).Reason);
        Assert.Empty(outcome.Moved);
        Assert.True(File.Exists(a));
        Assert.Contains(node, screen.Root.Children);
        Assert.Equal(300, screen.Root.Bytes);
    }

    [Fact]
    public void Undo_FromTheJournal_PutsItBack()
    {
        using var setup = new Setup();
        string a = setup.Add(@"A\a.bin", 300);
        DiskMapScreen screen = setup.Screen();
        screen.TrySelect(Child(screen.Root, "A"));

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);
        setup.Runner.Undo(outcome.Moved[0].Id);

        Assert.True(File.Exists(a));
    }

    // What moved is still on the drive until the bin is emptied, not unread.
    [Fact]
    public void Move_LeavesTheUnreadBoxAsItWas()
    {
        using var setup = new Setup();
        setup.Add(@"A\a.bin", 300);
        setup.Add("b.bin", 200);
        DiskMapScreen screen = setup.Screen(usedBytes: 1_000);
        screen.TrySelect(Child(screen.Root, "A"));

        screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Equal(500, screen.Boxes(688, 416).Single(box => box.Item.Kind == MapItemKind.Unread).Item.Bytes);
    }

    [Fact]
    public void AnInnerBox_CanBeEnteredFromAbove()
    {
        using var setup = new Setup();
        setup.Add(@"A\Inner\Deep\a.bin", 300);
        DiskMapScreen screen = setup.Screen();
        FolderNode a = Child(screen.Root, "A");
        FolderNode inner = Child(a, "Inner");

        screen.Enter(inner);

        Assert.Equal([screen.Root, a, inner], screen.Trail);
    }

    [Fact]
    public void Boxes_AreOfTheFolderBeingViewed()
    {
        using var setup = new Setup();
        setup.Add(@"A\x.bin", 300);
        setup.Add(@"A\y.bin", 200);
        DiskMapScreen screen = setup.Screen();

        screen.Enter(Child(screen.Root, "A"));

        Assert.Equal(["x.bin", "y.bin"], screen.Boxes(688, 416).Select(box => box.Item.Node!.Name));
    }
}
