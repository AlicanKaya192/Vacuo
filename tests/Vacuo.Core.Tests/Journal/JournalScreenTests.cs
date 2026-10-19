using Vacuo.Core.Actions;
using Vacuo.Core.Journal;
using Vacuo.Core.Tests.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Journal;

public class JournalScreenTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddMinutes(5);

    private sealed class Setup : IDisposable
    {
        public Setup()
        {
            Bin = new FakeRecycleBin(Tree.PathOf("bin"), new FixedTimeProvider(Earlier));
            Journal = new ActionJournal(Tree.PathOf("journal.jsonl"));
            Screen = new JournalScreen(Journal, RunnerAt(Later));
        }

        public TempTree Tree { get; } = new();
        public FakeRecycleBin Bin { get; }
        public ActionJournal Journal { get; }
        public JournalScreen Screen { get; }

        public ActionRunner RunnerAt(DateTimeOffset time) => new(Journal, Bin, new FixedTimeProvider(time));

        public JournalEntry Recycle(string relativePath, DateTimeOffset at)
        {
            string path = Tree.AddFile(relativePath, 100);
            return RunnerAt(at).Run(new MoveToRecycleBinAction(path, Bin));
        }

        public void Dispose() => Tree.Dispose();
    }

    [Fact]
    public void Entries_AreListedNewestFirst()
    {
        using var setup = new Setup();
        JournalEntry first = setup.Recycle(@"a\cache.bin", Earlier);
        JournalEntry second = setup.Recycle(@"b\cache.bin", Later);

        Assert.Equal([second.Id, first.Id], setup.Screen.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public void Undo_PutsTheItemBackAndMarksTheEntryUndone()
    {
        using var setup = new Setup();
        JournalEntry entry = setup.Recycle(@"a\cache.bin", Earlier);

        UndoFailureReason? failure = setup.Screen.Undo(entry.Id);

        Assert.Null(failure);
        Assert.True(File.Exists(entry.Undo.OriginalPath));
        Assert.Equal(0, setup.Bin.ItemCount);
        Assert.Equal(Later, setup.Screen.Entries.Single().UndoneAt);
    }

    [Fact]
    public void Undo_Twice_SecondTimeIsAlreadyUndone()
    {
        using var setup = new Setup();
        JournalEntry entry = setup.Recycle(@"a\cache.bin", Earlier);
        setup.Screen.Undo(entry.Id);

        Assert.Equal(UndoFailureReason.AlreadyUndone, setup.Screen.Undo(entry.Id));
    }

    [Fact]
    public void Undo_SomethingNewAtTheOriginalPath_IsLeftAloneAndTheItemStaysInTheBin()
    {
        using var setup = new Setup();
        JournalEntry entry = setup.Recycle(@"a\cache.bin", Earlier);
        setup.Tree.AddFile(@"a\cache.bin", 7);

        UndoFailureReason? failure = setup.Screen.Undo(entry.Id);

        Assert.Equal(UndoFailureReason.PathTaken, failure);
        Assert.Equal(7, new FileInfo(entry.Undo.OriginalPath).Length);
        Assert.Equal(1, setup.Bin.ItemCount);
        Assert.Null(setup.Screen.Entries.Single().UndoneAt);
    }

    [Fact]
    public void Undo_OriginalFolderGone_IsReportedAndNothingMoves()
    {
        using var setup = new Setup();
        JournalEntry entry = setup.Recycle(@"a\b\cache.bin", Earlier);
        Directory.Move(setup.Tree.PathOf(@"a\b"), setup.Tree.PathOf(@"a\moved"));

        Assert.Equal(UndoFailureReason.FolderGone, setup.Screen.Undo(entry.Id));
        Assert.Equal(1, setup.Bin.ItemCount);
        Assert.Null(setup.Screen.Entries.Single().UndoneAt);
    }

    [Fact]
    public void Undo_ItemNoLongerInTheBin_IsReportedNotInBin()
    {
        using var setup = new Setup();
        JournalEntry entry = setup.Recycle(@"a\cache.bin", Earlier);
        // The user restored or emptied it outside Vacuo: here it is moved out of the fake bin.
        File.Move(((RecycledItem)entry.Undo).BinPath, setup.Tree.PathOf("elsewhere.bin"));

        Assert.Equal(UndoFailureReason.NotInBin, setup.Screen.Undo(entry.Id));
        Assert.False(File.Exists(entry.Undo.OriginalPath));
        Assert.Null(setup.Screen.Entries.Single().UndoneAt);
    }
}
