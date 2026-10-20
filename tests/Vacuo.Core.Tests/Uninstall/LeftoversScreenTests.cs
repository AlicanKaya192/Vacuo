using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;
using Vacuo.Core.Scanning;
using Vacuo.Core.Tests.Actions;
using Vacuo.Core.Tests.Scanning;
using Vacuo.Core.Uninstall;

namespace Vacuo.Core.Tests.Uninstall;

public class LeftoversScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 20, 0, 0, TimeSpan.Zero);

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

        public LeftoverFolder Folder(string relativePath, int sizeBytes)
        {
            Tree.AddFile(Path.Combine(relativePath, "data.bin"), sizeBytes);
            return new LeftoverFolder(Tree.PathOf(relativePath), new DirectorySize(sizeBytes, 1));
        }

        public void Dispose() => Tree.Dispose();
    }

    [Fact]
    public void NewScreen_HasNothingPicked()
    {
        using var setup = new Setup();
        var screen = new LeftoversScreen([setup.Folder(@"Roaming\Foo Player", 40)]);

        Assert.Equal(0, screen.PickedCount);
        Assert.Equal(0, screen.PickedBytes);
    }

    [Fact]
    public void PickAndUnpick_UpdateCountAndBytes()
    {
        using var setup = new Setup();
        LeftoverFolder roaming = setup.Folder(@"Roaming\Foo Player", 40);
        LeftoverFolder local = setup.Folder(@"Local\Foo Player", 60);
        var screen = new LeftoversScreen([roaming, local]);

        screen.Pick(roaming.Path);
        screen.Pick(local.Path);
        screen.Unpick(roaming.Path);

        Assert.False(screen.IsPicked(roaming.Path));
        Assert.True(screen.IsPicked(local.Path));
        Assert.Equal(1, screen.PickedCount);
        Assert.Equal(60, screen.PickedBytes);
    }

    [Fact]
    public void Pick_FolderNotSuggested_IsRefused()
    {
        using var setup = new Setup();
        var screen = new LeftoversScreen([setup.Folder(@"Roaming\Foo Player", 40)]);

        Assert.Throws<ArgumentException>(() => screen.Pick(setup.Tree.PathOf("Roaming")));
    }

    [Fact]
    public void MoveSelected_SendsOnlyThePickedFoldersToTheBinAsALeftoversBatch()
    {
        using var setup = new Setup();
        LeftoverFolder roaming = setup.Folder(@"Roaming\Foo Player", 40);
        LeftoverFolder local = setup.Folder(@"Local\Foo Player", 60);
        var screen = new LeftoversScreen([roaming, local]);
        screen.Pick(local.Path);

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Empty(outcome.Failed);
        Assert.Equal(60, outcome.FreedBytes);
        Assert.False(Directory.Exists(local.Path));
        Assert.True(Directory.Exists(roaming.Path));
        JournalEntry entry = Assert.Single(setup.Journal.ReadAll());
        Assert.NotNull(entry.BatchId);
        Assert.Equal(JournalBatchKind.Leftovers, entry.BatchKind);
        Assert.Equal([roaming], screen.Folders);
        Assert.Equal(0, screen.PickedCount);
    }

    [Fact]
    public void MoveSelected_FolderGone_IsReportedMissing()
    {
        using var setup = new Setup();
        LeftoverFolder local = setup.Folder(@"Local\Foo Player", 60);
        var screen = new LeftoversScreen([local]);
        screen.Pick(local.Path);
        Directory.Delete(local.Path, recursive: true);

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Equal([new MoveFailure(local.Path, MoveFailureReason.Missing)], outcome.Failed);
        Assert.Equal(0, setup.Bin.ItemCount);
    }

    [Fact]
    public void MoveSelected_FileInUse_StaysPickedAndIsReported()
    {
        using var setup = new Setup();
        LeftoverFolder local = setup.Folder(@"Local\Foo Player", 60);
        var screen = new LeftoversScreen([local]);
        screen.Pick(local.Path);
        using FileStream open = OpenFileProbeTests.OpenLikeAnotherProgram(Path.Combine(local.Path, "data.bin"));

        MoveOutcome outcome = screen.MoveSelected(setup.Runner, setup.Bin);

        Assert.Equal([new MoveFailure(local.Path, MoveFailureReason.InUse)], outcome.Failed);
        Assert.True(screen.IsPicked(local.Path));
    }
}
