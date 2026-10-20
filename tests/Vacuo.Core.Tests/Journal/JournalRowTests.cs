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

    [Fact]
    public void Batch_IsUndoneOnlyWhenEveryFileIsBack()
    {
        JournalRow partly = Assert.Single(JournalRow.From([Folder(), Moved("a.pdf", Later), Moved("b.pdf")]));
        JournalRow whole = Assert.Single(JournalRow.From([Folder(), Moved("a.pdf", Later), Moved("b.pdf", Later.AddSeconds(1))]));

        Assert.Null(partly.UndoneAt);
        Assert.Equal(Later.AddSeconds(1), whole.UndoneAt);
    }
}
