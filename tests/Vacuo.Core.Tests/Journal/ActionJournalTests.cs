using Vacuo.Core.Actions;
using Vacuo.Core.Journal;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Journal;

public class ActionJournalTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 19, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Evening = new(2026, 10, 19, 18, 45, 0, TimeSpan.Zero);

    private static JournalEntry MovedEntry() =>
        new(Guid.NewGuid(), Morning, 1234, new MovedItem(@"C:\Users\x\Desktop\a.txt", @"C:\Users\x\Documents\a.txt"), UndoneAt: null);

    private static JournalEntry RecycledEntry() =>
        new(Guid.NewGuid(), Evening, 4_200_000_000, new RecycledItem(@"C:\Users\x\AppData\Local\pip\Cache", Evening, @"C:\$Recycle.Bin\user\$RAB12CD"), UndoneAt: null);

    [Fact]
    public void ReadAll_NoJournalFileYet_IsEmpty()
    {
        using var tree = new TempTree();

        Assert.Empty(new ActionJournal(tree.PathOf(@"Vacuo\journal.jsonl")).ReadAll());
    }

    [Fact]
    public void Append_EntriesOfBothKinds_AreReadBackByANewJournalOnTheSameFile()
    {
        using var tree = new TempTree();
        string file = tree.PathOf(@"Vacuo\journal.jsonl");
        JournalEntry moved = MovedEntry();
        JournalEntry recycled = RecycledEntry();

        var journal = new ActionJournal(file);
        journal.Append(moved);
        journal.Append(recycled);

        Assert.Equal([moved, recycled], new ActionJournal(file).ReadAll());
    }

    [Fact]
    public void Append_BatchKind_IsReadBack()
    {
        using var tree = new TempTree();
        string file = tree.PathOf(@"Vacuo\journal.jsonl");
        var journal = new ActionJournal(file);
        JournalEntry labelled = RecycledEntry() with { BatchId = Guid.NewGuid(), BatchKind = JournalBatchKind.Leftovers };
        JournalEntry unlabelled = MovedEntry() with { BatchId = Guid.NewGuid() };

        journal.Append(labelled);
        journal.Append(unlabelled);

        Assert.Equal([labelled, unlabelled], new ActionJournal(file).ReadAll());
    }

    [Fact]
    public void ReadAll_LineWrittenBeforeBatchKindExisted_HasNoBatchKind()
    {
        using var tree = new TempTree();
        string file = tree.PathOf(@"Vacuo\journal.jsonl");
        JournalEntry entry = MovedEntry() with { BatchId = Guid.NewGuid() };
        new ActionJournal(file).Append(entry);
        string line = File.ReadAllText(file);
        Assert.Contains(",\"BatchKind\":null", line);
        File.WriteAllText(file, line.Replace(",\"BatchKind\":null", string.Empty));

        Assert.Equal([entry], new ActionJournal(file).ReadAll());
    }

    [Fact]
    public void MarkUndone_IsReadBackAsTheUndoTime()
    {
        using var tree = new TempTree();
        var journal = new ActionJournal(tree.PathOf("journal.jsonl"));
        JournalEntry entry = MovedEntry();
        journal.Append(entry);

        journal.MarkUndone(entry.Id, Evening);

        Assert.Equal(entry with { UndoneAt = Evening }, Assert.Single(journal.ReadAll()));
    }

    [Fact]
    public void Append_LeavesEarlierLinesByteForByteUntouched()
    {
        using var tree = new TempTree();
        string file = tree.PathOf("journal.jsonl");
        var journal = new ActionJournal(file);
        journal.Append(MovedEntry());
        byte[] before = File.ReadAllBytes(file);

        journal.Append(RecycledEntry());
        journal.MarkUndone(Guid.NewGuid(), Evening);

        Assert.Equal(before, File.ReadAllBytes(file).Take(before.Length));
    }

    [Fact]
    public void ReadAll_CorruptLine_ThrowsNamingTheLine()
    {
        using var tree = new TempTree();
        string file = tree.PathOf("journal.jsonl");
        var journal = new ActionJournal(file);
        journal.Append(MovedEntry());
        File.AppendAllText(file, "{ not json\n");

        var error = Assert.Throws<JournalCorruptException>(() => journal.ReadAll());

        Assert.Equal(2, error.LineNumber);
    }

    [Fact]
    public void DefaultPath_IsVacuoFolderUnderLocalAppData()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(localAppData, "Vacuo", "journal.jsonl"), ActionJournal.DefaultPath);
    }
}
