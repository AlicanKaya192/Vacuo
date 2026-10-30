using Vacuo.Core.Disk;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Disk;

public class DiskHistoryStoreTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);

    private static readonly DiskSnapshot Measured = new(Today, 1_000, [new FolderBytes(@"C:\Users\Someone\Downloads", 300)]);

    [Fact]
    public void Load_WithoutAFile_IsEmpty()
    {
        using var tree = new TempTree();

        Assert.Empty(new DiskHistoryStore(tree.PathOf("disk-history.json")).Load().Snapshots);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"Snapshots":null}""")]
    [InlineData("""{"Snapshots":[null]}""")]
    [InlineData("""{"Snapshots":[{"Day":"2026-10-30","UsedBytes":1,"Folders":null}]}""")]
    [InlineData("""{"Snapshots":[{"Day":"2026-10-30","UsedBytes":1,"Folders":[null]}]}""")]
    [InlineData("""{"Snapshots":[{"Day":"2026-10-30","UsedBytes":1,"Folders":[{"Path":null,"Bytes":1}]}]}""")]
    public void Load_ADamagedFile_IsEmpty(string content)
    {
        using var tree = new TempTree();
        File.WriteAllText(tree.PathOf("disk-history.json"), content);

        Assert.Empty(new DiskHistoryStore(tree.PathOf("disk-history.json")).Load().Snapshots);
    }

    [Fact]
    public void Record_IsThereWhenLoadedAgain()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("disk-history.json");

        new DiskHistoryStore(path).Record(Measured);

        DiskSnapshot loaded = Assert.Single(new DiskHistoryStore(path).Load().Snapshots);
        Assert.Equal(Today, loaded.Day);
        Assert.Equal(1_000, loaded.UsedBytes);
        Assert.Equal(new FolderBytes(@"C:\Users\Someone\Downloads", 300), Assert.Single(loaded.Folders));
    }

    [Fact]
    public void Record_AddsToWhatIsAlreadyThere_AndReturnsTheWhole()
    {
        using var tree = new TempTree();
        var store = new DiskHistoryStore(tree.PathOf("disk-history.json"));
        store.Record(Measured with { Day = Today.AddDays(-1) });

        DiskHistory history = store.Record(Measured);

        Assert.Equal([Today.AddDays(-1), Today], history.Snapshots.Select(snapshot => snapshot.Day));
    }

    [Fact]
    public void Record_LeavesNoTemporaryFile()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("disk-history.json");

        new DiskHistoryStore(path).Record(Measured);

        Assert.False(File.Exists(path + ".tmp"));
    }

    // The history is a nicety: a file that cannot be written must not stop the Disk screen.
    [Fact]
    public void Record_ThatCannotBeWritten_StillReturnsTheHistory()
    {
        using var tree = new TempTree();
        File.WriteAllText(tree.PathOf("occupied"), "a file where the folder should be");
        var store = new DiskHistoryStore(Path.Combine(tree.PathOf("occupied"), "disk-history.json"));

        DiskHistory history = store.Record(Measured);

        Assert.Equal(Today, Assert.Single(history.Snapshots).Day);
    }
}
