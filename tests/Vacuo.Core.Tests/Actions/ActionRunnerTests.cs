using Vacuo.Core.Actions;
using Vacuo.Core.Journal;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Actions;

public class ActionRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed class Setup : IDisposable
    {
        public Setup()
        {
            var time = new FixedTimeProvider(Now);
            Bin = new FakeRecycleBin(Tree.PathOf("bin"), time);
            Journal = new ActionJournal(Tree.PathOf(@"Vacuo\journal.jsonl"));
            Runner = new ActionRunner(Journal, Bin, time);
        }

        public TempTree Tree { get; } = new();
        public FakeRecycleBin Bin { get; }
        public ActionJournal Journal { get; }
        public ActionRunner Runner { get; }

        public void Dispose() => Tree.Dispose();
    }

    [Fact]
    public void Run_RecordsTimeSizeAndHowToUndo()
    {
        using var setup = new Setup();
        setup.Tree.AddFile(@"web\node_modules\a.js", 300);
        setup.Tree.AddFile(@"web\node_modules\lib\b.js", 200);
        string target = setup.Tree.PathOf(@"web\node_modules");

        JournalEntry entry = setup.Runner.Run(new MoveToRecycleBinAction(target, setup.Bin));

        Assert.Equal(Now, entry.At);
        Assert.Equal(500, entry.SizeBytes);
        Assert.Equal(target, Assert.IsType<RecycledItem>(entry.Undo).OriginalPath);
        Assert.Null(entry.UndoneAt);
        Assert.Equal([entry], setup.Journal.ReadAll());
    }

    [Fact]
    public void RecordDeletedForGood_WritesAnEntryThatSaysItCannotBeUndone()
    {
        using var setup = new Setup();

        JournalEntry entry = setup.Runner.RecordDeletedForGood(@"C:\Windows\Temp", 4096);

        Assert.Equal(Now, entry.At);
        Assert.Equal(4096, entry.SizeBytes);
        Assert.Equal(@"C:\Windows\Temp", Assert.IsType<DeletedForGood>(entry.Undo).OriginalPath);
        Assert.Equal([entry], setup.Journal.ReadAll());
    }

    [Fact]
    public void Undo_DeletedForGood_RefusesAndMarksNothing()
    {
        using var setup = new Setup();
        JournalEntry entry = setup.Runner.RecordDeletedForGood(setup.Tree.PathOf("gone"), 10);

        Assert.Throws<ActionRefusedException>(() => setup.Runner.Undo(entry.Id));
        Assert.Null(setup.Journal.ReadAll().Single().UndoneAt);
    }

    [Fact]
    public void Run_FolderWithAFileInUse_MovesNothingAndRecordsNothing()
    {
        using var setup = new Setup();
        setup.Tree.AddFile(@"ml\.venv\pyvenv.cfg", 5);
        string busy = setup.Tree.AddFile(@"ml\.venv\Scripts\python.exe", 50);
        using FileStream running = OpenFileProbeTests.OpenLikeAnotherProgram(busy);

        Assert.Throws<FileInUseException>(
            () => setup.Runner.Run(new MoveToRecycleBinAction(setup.Tree.PathOf(@"ml\.venv"), setup.Bin)));

        Assert.True(File.Exists(setup.Tree.PathOf(@"ml\.venv\pyvenv.cfg")));
        Assert.Equal(0, setup.Bin.ItemCount);
        Assert.Empty(setup.Journal.ReadAll());
    }

    [Fact]
    public void Undo_Move_PutsTheItemBackAndMarksTheEntryUndone()
    {
        using var setup = new Setup();
        string source = setup.Tree.AddFile(@"Desktop\notes.txt", 10);
        setup.Tree.AddDirectory("Documents");
        JournalEntry entry = setup.Runner.Run(new MoveFileAction(source, setup.Tree.PathOf(@"Documents\notes.txt")));

        setup.Runner.Undo(entry.Id);

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(setup.Tree.PathOf(@"Documents\notes.txt")));
        Assert.Equal(Now, Assert.Single(setup.Journal.ReadAll()).UndoneAt);
    }

    [Fact]
    public void Undo_Recycle_RestoresFromTheRecycleBin()
    {
        using var setup = new Setup();
        string cache = setup.Tree.AddFile(@"Local\pip\Cache\entry", 30);
        JournalEntry entry = setup.Runner.Run(new MoveToRecycleBinAction(setup.Tree.PathOf(@"Local\pip\Cache"), setup.Bin));

        setup.Runner.Undo(entry.Id);

        Assert.True(File.Exists(cache));
        Assert.Equal(0, setup.Bin.ItemCount);
    }

    [Fact]
    public void Undo_OriginalPathTakenAgain_RefusesAndOverwritesNothing()
    {
        using var setup = new Setup();
        string source = setup.Tree.AddFile(@"Desktop\notes.txt", 10);
        setup.Tree.AddDirectory("Documents");
        JournalEntry entry = setup.Runner.Run(new MoveFileAction(source, setup.Tree.PathOf(@"Documents\notes.txt")));
        setup.Tree.AddFile(@"Desktop\notes.txt", 77);

        Assert.Throws<ActionRefusedException>(() => setup.Runner.Undo(entry.Id));

        Assert.Equal(77, new FileInfo(source).Length);
        Assert.Equal(10, new FileInfo(setup.Tree.PathOf(@"Documents\notes.txt")).Length);
        Assert.Null(Assert.Single(setup.Journal.ReadAll()).UndoneAt);
    }

    [Fact]
    public void Undo_SameEntryTwice_RefusesTheSecondTime()
    {
        using var setup = new Setup();
        string source = setup.Tree.AddFile(@"Desktop\notes.txt", 10);
        setup.Tree.AddDirectory("Documents");
        string destination = setup.Tree.PathOf(@"Documents\notes.txt");
        JournalEntry entry = setup.Runner.Run(new MoveFileAction(source, destination));
        setup.Runner.Undo(entry.Id);
        File.Move(source, destination);

        Assert.Throws<ActionRefusedException>(() => setup.Runner.Undo(entry.Id));

        Assert.True(File.Exists(destination));
    }

    [Fact]
    public void Undo_RecycledItemWhosePlaceIsTakenAgain_RefusesAndLeavesItInTheBin()
    {
        using var setup = new Setup();
        setup.Tree.AddFile(@"Local\pip\Cache\entry", 30);
        JournalEntry entry = setup.Runner.Run(new MoveToRecycleBinAction(setup.Tree.PathOf(@"Local\pip\Cache"), setup.Bin));
        setup.Tree.AddFile(@"Local\pip\Cache\new-entry", 5);

        Assert.Throws<ActionRefusedException>(() => setup.Runner.Undo(entry.Id));

        Assert.Equal(1, setup.Bin.ItemCount);
    }

    [Fact]
    public void Undo_UnknownEntry_Refuses()
    {
        using var setup = new Setup();

        Assert.Throws<ActionRefusedException>(() => setup.Runner.Undo(Guid.NewGuid()));
    }
}
