using Vacuo.Core.Actions;
using Vacuo.Core.Journal;
using Vacuo.Core.Tests.Scanning;
using Vacuo.Windows;

namespace Vacuo.Windows.Tests;

/// <summary>
/// These tests use the user's real Recycle Bin, so they are explicit: a normal test run
/// skips them, and they run only on request (<c>--explicit only</c>). Each one puts a
/// throwaway item from the temp folder into the bin and takes it back out.
/// </summary>
public class ShellRecycleBinTests
{
    private const string FileContent = "Vacuo recycle bin test";

    [Fact(Explicit = true)]
    public void RunAndUndo_File_GoesToTheRecycleBinAndComesBackUnchanged()
    {
        using var tree = new TempTree();
        string file = tree.PathOf("vacuo-test-recycle.txt");
        File.WriteAllText(file, FileContent);
        var bin = new ShellRecycleBin(TimeProvider.System);
        var runner = new ActionRunner(new ActionJournal(tree.PathOf("journal.jsonl")), bin, TimeProvider.System);

        JournalEntry entry = runner.Run(new MoveToRecycleBinAction(file, bin));
        var recycled = Assert.IsType<RecycledItem>(entry.Undo);
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(recycled.BinPath));

        runner.Undo(entry.Id);

        Assert.Equal(FileContent, File.ReadAllText(file));
        Assert.False(File.Exists(recycled.BinPath));
    }

    [Fact(Explicit = true)]
    public void MoveToBinAndRestore_FolderWithJunction_LeavesTheJunctionTargetAlone()
    {
        using var tree = new TempTree();
        tree.AddFile(@"vacuo-test-project\node_modules\lib\index.js", 50);
        string keep = tree.AddFile(@"elsewhere\keep.bin", 70);
        tree.AddJunction(@"vacuo-test-project\node_modules\link", "elsewhere");
        string folder = tree.PathOf(@"vacuo-test-project\node_modules");
        var bin = new ShellRecycleBin(TimeProvider.System);

        RecycledItem item = bin.MoveToBin(folder);
        Assert.False(Directory.Exists(folder));
        Assert.Equal(70, new FileInfo(keep).Length);

        bin.Restore(item);

        Assert.True(File.Exists(Path.Combine(folder, @"lib\index.js")));
        Assert.True(new DirectoryInfo(Path.Combine(folder, "link")).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(70, new FileInfo(keep).Length);
    }

    [Fact(Explicit = true)]
    public void Restore_ItemNoLongerInTheBin_Refuses()
    {
        using var tree = new TempTree();
        var missing = new RecycledItem(tree.PathOf("vacuo-test-gone.txt"), DateTimeOffset.UtcNow, @"C:\$Recycle.Bin\none\$RNOTHERE.txt");

        Assert.Throws<ItemNotInBinException>(() => new ShellRecycleBin(TimeProvider.System).Restore(missing));
    }
}
