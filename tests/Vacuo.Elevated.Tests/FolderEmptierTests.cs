using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Elevated.Tests;

public class FolderEmptierTests
{
    [Fact]
    public void Empty_DeletesFilesAndSubfoldersButKeepsTheFolder()
    {
        using var tree = new TempTree();
        tree.AddFile(@"temp\a.tmp", 100);
        tree.AddFile(@"temp\cab\b.tmp", 40);
        tree.AddFile(@"temp\cab\deep\c.tmp", 10);

        EmptyResult result = FolderEmptier.Empty(tree.PathOf("temp"));

        Assert.Equal(new EmptyResult(DeletedBytes: 150, DeletedFiles: 3, Skipped: 0), result);
        Assert.True(Directory.Exists(tree.PathOf("temp")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(tree.PathOf("temp")));
    }

    [Fact]
    public void Empty_LinkInside_IsLeftAloneAndItsTargetIsUntouched()
    {
        using var tree = new TempTree();
        string keep = tree.AddFile(@"elsewhere\keep.bin", 70);
        tree.AddFile(@"temp\a.tmp", 5);
        tree.AddJunction(@"temp\link", "elsewhere");

        EmptyResult result = FolderEmptier.Empty(tree.PathOf("temp"));

        Assert.Equal(new EmptyResult(DeletedBytes: 5, DeletedFiles: 1, Skipped: 1), result);
        Assert.True(File.Exists(keep));
        Assert.True(Directory.Exists(tree.PathOf(@"temp\link")));
    }

    [Fact]
    public void Empty_FileInUse_IsSkippedAndCountedAndTheRestGoes()
    {
        using var tree = new TempTree();
        string busy = tree.AddFile(@"temp\busy.tmp", 30);
        tree.AddFile(@"temp\free.tmp", 20);

        EmptyResult result;
        using (new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = FolderEmptier.Empty(tree.PathOf("temp"));
        }

        Assert.Equal(new EmptyResult(DeletedBytes: 20, DeletedFiles: 1, Skipped: 1), result);
        Assert.True(File.Exists(busy));
    }

    [Fact]
    public void Empty_MissingFolder_DoesNothing()
    {
        using var tree = new TempTree();

        Assert.Equal(new EmptyResult(0, 0, 0), FolderEmptier.Empty(tree.PathOf("no-such-folder")));
    }

    [Fact]
    public void Empty_FolderThatIsALink_IsNotEntered()
    {
        using var tree = new TempTree();
        string keep = tree.AddFile(@"elsewhere\keep.bin", 70);
        tree.AddJunction("temp", "elsewhere");

        EmptyResult result = FolderEmptier.Empty(tree.PathOf("temp"));

        Assert.Equal(new EmptyResult(0, 0, Skipped: 1), result);
        Assert.True(File.Exists(keep));
    }
}
