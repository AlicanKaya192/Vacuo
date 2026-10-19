using Vacuo.Core.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Actions;

public class MoveFileActionTests
{
    [Fact]
    public void Execute_File_MovesItAndReturnsHowToMoveItBack()
    {
        using var tree = new TempTree();
        string source = tree.AddFile(@"Downloads\setup.exe", 40);
        string destination = tree.PathOf(@"Downloads\Installers\setup.exe");
        tree.AddDirectory(@"Downloads\Installers");

        UndoRecord undo = new MoveFileAction(source, destination).Execute();

        Assert.False(File.Exists(source));
        Assert.Equal(40, new FileInfo(destination).Length);
        Assert.Equal(new MovedItem(source, destination), undo);
    }

    [Fact]
    public void Execute_Folder_MovesItWithEverythingInside()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Desktop\photos\a.jpg", 10);
        tree.AddFile(@"Desktop\photos\trip\b.jpg", 20);
        string destination = tree.PathOf(@"Pictures\photos");
        tree.AddDirectory("Pictures");

        new MoveFileAction(tree.PathOf(@"Desktop\photos"), destination).Execute();

        Assert.False(Directory.Exists(tree.PathOf(@"Desktop\photos")));
        Assert.True(File.Exists(Path.Combine(destination, @"trip\b.jpg")));
    }

    [Fact]
    public void Execute_DestinationExists_RefusesAndLeavesBothUntouched()
    {
        using var tree = new TempTree();
        string source = tree.AddFile(@"Desktop\notes.txt", 10);
        string destination = tree.AddFile(@"Documents\notes.txt", 99);

        Assert.Throws<ActionRefusedException>(() => new MoveFileAction(source, destination).Execute());

        Assert.Equal(10, new FileInfo(source).Length);
        Assert.Equal(99, new FileInfo(destination).Length);
    }

    [Fact]
    public void Execute_DestinationOnAnotherDrive_RefusesBeforeTouchingAnything()
    {
        using var tree = new TempTree();
        string source = tree.AddFile(@"Desktop\notes.txt", 10);
        string otherDrive = Path.GetPathRoot(source)!.StartsWith('Z') ? @"Y:\" : @"Z:\";

        Assert.Throws<ActionRefusedException>(
            () => new MoveFileAction(source, Path.Combine(otherDrive, "notes.txt")).Execute());

        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Execute_SourceMissing_Refuses()
    {
        using var tree = new TempTree();

        Assert.Throws<ActionRefusedException>(
            () => new MoveFileAction(tree.PathOf("gone.txt"), tree.PathOf("elsewhere.txt")).Execute());
    }
}
