using Vacuo.Core.Actions;
using Vacuo.Core.Journal;

namespace Vacuo.Core.Tests.Journal;

public class JournalRowTests
{
    private const string Desktop = @"C:\Users\test\Desktop";
    private static readonly DateTimeOffset Earlier = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddMinutes(5);
    private static readonly Guid Batch = Guid.NewGuid();

    private static JournalEntry Moved(string name, DateTimeOffset? undoneAt = null) => new(
        Guid.NewGuid(), Later, 10, new MovedItem(Path.Combine(Desktop, name), Path.Combine(Desktop, "Documents", name)), undoneAt, Batch);

    private static JournalEntry Folder() => new(Guid.NewGuid(), Later, 0, new CreatedFolder(Path.Combine(Desktop, "Documents")), null, Batch);

    private static JournalEntry Recycled() => new(
        Guid.NewGuid(), Earlier, 300, new RecycledItem(@"C:\Users\test\web\node_modules", Earlier, @"C:\$Recycle.Bin\x"), null);

    [Fact]
    public void OneOrganizeRun_IsOneRow_NewestFirst()
    {
        IReadOnlyList<JournalRow> rows = JournalRow.From([Recycled(), Folder(), Moved("a.pdf"), Moved("b.pdf")]);

        Assert.Equal(2, rows.Count);
        Assert.Equal(Batch, rows[0].BatchId);
        Assert.Equal(2, rows[0].FileCount);
        Assert.Equal(20, rows[0].SizeBytes);
        Assert.Equal(Desktop, rows[0].Folder);
        Assert.Null(rows[1].BatchId);
    }

    private static JournalEntry RecycledIn(Guid batch, string path, JournalBatchKind? kind = null) =>
        new(Guid.NewGuid(), Later, 100, new RecycledItem(path, Later, @"C:\$Recycle.Bin\x"), null, batch, kind);

    // Lines written before batches carried their kind: recycled-only runs came from the duplicates screen.
    [Fact]
    public void AnUnlabelledRunOfRecycledCopies_IsADuplicatesRunAcrossItsFolders()
    {
        var copies = Guid.NewGuid();

        IReadOnlyList<JournalRow> rows = JournalRow.From(
        [
            RecycledIn(copies, @"C:\Users\test\Downloads\a.pdf"),
            RecycledIn(copies, @"C:\Users\test\Desktop\b.jpg"),
            RecycledIn(copies, @"C:\Users\test\Downloads\c.pdf"),
            Folder(),
            Moved("d.pdf"),
        ]);

        JournalRow duplicates = Assert.Single(rows, row => row.BatchId == copies);
        Assert.Equal(JournalBatchKind.Duplicates, duplicates.BatchKind);
        Assert.Equal([@"C:\Users\test\Downloads", @"C:\Users\test\Desktop"], duplicates.Folders);
        Assert.Equal(JournalBatchKind.Organize, Assert.Single(rows, row => row.BatchId == Batch).BatchKind);
    }

    [Fact]
    public void ALabelledRun_KeepsItsKind()
    {
        var leftovers = Guid.NewGuid();

        JournalRow row = Assert.Single(JournalRow.From([RecycledIn(leftovers, @"C:\Users\test\AppData\Roaming\Foo", JournalBatchKind.Leftovers)]));

        Assert.Equal(JournalBatchKind.Leftovers, row.BatchKind);
    }

    [Fact]
    public void ASingleEntry_HasNoBatchKind()
    {
        Assert.Null(Assert.Single(JournalRow.From([Recycled()])).BatchKind);
    }

    [Fact]
    public void Batch_IsUndoneOnlyWhenEveryFileIsBack()
    {
        JournalRow partly = Assert.Single(JournalRow.From([Folder(), Moved("a.pdf", Later), Moved("b.pdf")]));
        JournalRow whole = Assert.Single(JournalRow.From([Folder(), Moved("a.pdf", Later), Moved("b.pdf", Later.AddSeconds(1))]));

        Assert.Null(partly.UndoneAt);
        Assert.Equal(Later.AddSeconds(1), whole.UndoneAt);
    }
}
