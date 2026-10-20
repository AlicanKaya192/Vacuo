using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Duplicates;
using Vacuo.Core.Journal;
using Vacuo.Core.Tests.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Duplicates;

public class DuplicatesScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 19, 0, 0, TimeSpan.Zero);

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

        public DuplicateGroup Group(int sizeBytes, params string[] relativePaths) => new(
            sizeBytes,
            relativePaths.Select(relative => new DuplicateFile(Tree.AddFile(relative, sizeBytes), DateTime.UnixEpoch)).ToList());

        public void Dispose() => Tree.Dispose();
    }

    private static DuplicatesScreen ScreenOf(params DuplicateGroup[] groups) => new(new DuplicateScan(groups, SkippedEntries: 0));

    [Fact]
    public void NewScreen_HasNothingPickedAndCountsTheWaste()
    {
        using var setup = new Setup();
        DuplicatesScreen screen = ScreenOf(
            setup.Group(300, @"Documents\a.pdf", @"Downloads\a.pdf", @"Desktop\a.pdf"),
            setup.Group(50, @"Pictures\b.jpg", @"Desktop\b.jpg"));

        Assert.Equal(0, screen.PickedCount);
        Assert.Equal(0, screen.PickedBytes);
        Assert.Empty(screen.Preview());
        Assert.Equal(650, screen.WastedBytes);
    }

    [Fact]
    public void CanPick_IsFalseForTheLastUnpickedCopy()
    {
        using var setup = new Setup();
        DuplicateGroup group = setup.Group(300, @"Documents\a.pdf", @"Downloads\a.pdf");
        DuplicatesScreen screen = ScreenOf(group);

        Assert.True(screen.TryPick(group.Files[0].Path));

        Assert.True(screen.CanPick(group.Files[0].Path));
        Assert.False(screen.CanPick(group.Files[1].Path));
        Assert.False(screen.TryPick(group.Files[1].Path));
    }

    [Fact]
    public void Preview_ListsThePickedCopiesLargestFirst()
    {
        using var setup = new Setup();
        DuplicateGroup small = setup.Group(50, @"Pictures\b.jpg", @"Desktop\b.jpg");
        DuplicateGroup large = setup.Group(300, @"Documents\a.pdf", @"Downloads\a.pdf");
        DuplicatesScreen screen = ScreenOf(large, small);
        screen.TryPick(small.Files[1].Path);
        screen.TryPick(large.Files[1].Path);

        Assert.Equal(
            [new PickedCopy(large.Files[1].Path, 300), new PickedCopy(small.Files[1].Path, 50)],
            screen.Preview());
        Assert.Equal(350, screen.PickedBytes);
    }

    [Fact]
    public void MoveSelected_SendsThePickedCopiesToTheBinAsOneJournalBatch()
    {
        using var setup = new Setup();
        DuplicateGroup pdf = setup.Group(300, @"Documents\a.pdf", @"Downloads\a.pdf", @"Desktop\a.pdf");
        DuplicateGroup jpg = setup.Group(50, @"Pictures\b.jpg", @"Desktop\b.jpg");
        DuplicatesScreen screen = ScreenOf(pdf, jpg);
        screen.TryPick(pdf.Files[1].Path);
        screen.TryPick(jpg.Files[1].Path);

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Empty(outcome.Failed);
        Assert.Equal(350, outcome.FreedBytes);
        Assert.False(File.Exists(pdf.Files[1].Path));
        Assert.False(File.Exists(jpg.Files[1].Path));
        Assert.True(File.Exists(pdf.Files[0].Path));
        Assert.True(File.Exists(jpg.Files[0].Path));
        IReadOnlyList<JournalEntry> journal = setup.Journal.ReadAll();
        Assert.Equal(2, journal.Count);
        Assert.NotNull(journal[0].BatchId);
        Assert.All(journal, entry => Assert.Equal(journal[0].BatchId, entry.BatchId));
        Assert.All(journal, entry => Assert.Equal(JournalBatchKind.Duplicates, entry.BatchKind));
    }

    [Fact]
    public void MoveSelected_AGroupLeftWithOneCopy_LeavesTheList()
    {
        using var setup = new Setup();
        DuplicateGroup pdf = setup.Group(300, @"Documents\a.pdf", @"Downloads\a.pdf", @"Desktop\a.pdf");
        DuplicateGroup jpg = setup.Group(50, @"Pictures\b.jpg", @"Desktop\b.jpg");
        DuplicatesScreen screen = ScreenOf(pdf, jpg);
        screen.TryPick(pdf.Files[1].Path);
        screen.TryPick(jpg.Files[1].Path);

        screen.MoveSelected(setup.Runner, setup.Bin);

        DuplicateGroup left = Assert.Single(screen.Groups);
        Assert.Equal([pdf.Files[0].Path, pdf.Files[2].Path], left.Files.Select(file => file.Path));
        Assert.Equal(0, screen.PickedCount);
        Assert.Equal(300, screen.WastedBytes);
    }

    [Fact]
    public void MoveSelected_KeptCopyGoneSinceTheScan_DoesNotMoveTheLastOne()
    {
        using var setup = new Setup();
        DuplicateGroup group = setup.Group(300, @"Documents\a.pdf", @"Downloads\a.pdf");
        DuplicatesScreen screen = ScreenOf(group);
        screen.TryPick(group.Files[1].Path);
        File.Delete(group.Files[0].Path);

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Equal([new MoveFailure(group.Files[1].Path, MoveFailureReason.NoLongerDuplicate)], outcome.Failed);
        Assert.True(File.Exists(group.Files[1].Path));
        Assert.Equal(0, setup.Bin.ItemCount);
    }

    [Fact]
    public void MoveSelected_PickedCopyChangedSinceTheScan_StaysPut()
    {
        using var setup = new Setup();
        DuplicateGroup group = setup.Group(300, @"Documents\a.pdf", @"Downloads\a.pdf");
        DuplicatesScreen screen = ScreenOf(group);
        screen.TryPick(group.Files[1].Path);
        File.AppendAllText(group.Files[1].Path, "edited");

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Equal([new MoveFailure(group.Files[1].Path, MoveFailureReason.NoLongerDuplicate)], outcome.Failed);
        Assert.True(File.Exists(group.Files[1].Path));
    }

    [Fact]
    public void MoveSelected_PickedCopyGone_IsReportedMissing()
    {
        using var setup = new Setup();
        DuplicateGroup group = setup.Group(300, @"Documents\a.pdf", @"Downloads\a.pdf", @"Desktop\a.pdf");
        DuplicatesScreen screen = ScreenOf(group);
        screen.TryPick(group.Files[1].Path);
        File.Delete(group.Files[1].Path);

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Equal([new MoveFailure(group.Files[1].Path, MoveFailureReason.Missing)], outcome.Failed);
    }

    [Fact]
    public void MoveSelected_CopyInUse_OthersStillMoveAndTheBusyOneStaysPicked()
    {
        using var setup = new Setup();
        DuplicateGroup pdf = setup.Group(300, @"Documents\a.pdf", @"Downloads\a.pdf");
        DuplicateGroup jpg = setup.Group(50, @"Pictures\b.jpg", @"Desktop\b.jpg");
        DuplicatesScreen screen = ScreenOf(pdf, jpg);
        screen.TryPick(pdf.Files[1].Path);
        screen.TryPick(jpg.Files[1].Path);
        using FileStream open = OpenFileProbeTests.OpenLikeAnotherProgram(pdf.Files[1].Path);

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Equal([new MoveFailure(pdf.Files[1].Path, MoveFailureReason.InUse)], outcome.Failed);
        Assert.False(File.Exists(jpg.Files[1].Path));
        Assert.True(screen.IsPicked(pdf.Files[1].Path));
    }

    [Fact]
    public void UndoingTheBatch_PutsEveryCopyBack()
    {
        using var setup = new Setup();
        DuplicateGroup pdf = setup.Group(300, @"Documents\a.pdf", @"Downloads\a.pdf");
        DuplicateGroup jpg = setup.Group(50, @"Pictures\b.jpg", @"Desktop\b.jpg");
        DuplicatesScreen screen = ScreenOf(pdf, jpg);
        screen.TryPick(pdf.Files[1].Path);
        screen.TryPick(jpg.Files[0].Path);
        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        IReadOnlyList<BatchUndoFailure> failures = new JournalScreen(setup.Journal, setup.Runner).UndoBatch(outcome.Moved[0].BatchId!.Value);

        Assert.Empty(failures);
        Assert.True(File.Exists(pdf.Files[1].Path));
        Assert.True(File.Exists(jpg.Files[0].Path));
    }
}
