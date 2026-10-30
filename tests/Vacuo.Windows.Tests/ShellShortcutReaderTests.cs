using Vacuo.Core.Organize;
using Vacuo.Core.Tests.Scanning;
using Vacuo.Windows;

namespace Vacuo.Windows.Tests;

public class ShellShortcutReaderTests
{
    // A real .lnk, written by Windows' own scripting host rather than by this code.
    private static string RealShortcut(TempTree tree, string name, string target)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
        string path = tree.PathOf(name);
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = target;
        shortcut.Save();
        return path;
    }

    [Fact]
    public void TargetOf_ReadsWhereARealShortcutPoints()
    {
        using var tree = new TempTree();
        const string target = @"C:\Program Files (x86)\Steam\steamapps\common\Some Game\game.exe";
        string shortcut = RealShortcut(tree, "game.lnk", target);

        Assert.Equal(target, new ShellShortcutReader().TargetOf(shortcut));
    }

    [Fact]
    public void ARealShortcutToAGame_IsAGame_AndToAnotherProgram_IsNot()
    {
        using var tree = new TempTree();
        string game = RealShortcut(tree, "game.lnk", @"D:\Riot Games\VALORANT\live\VALORANT.exe");
        string other = RealShortcut(tree, "other.lnk", @"C:\Windows\notepad.exe");
        var reader = new ShellShortcutReader();

        Assert.True(GameShortcut.IsGame(game, reader));
        Assert.False(GameShortcut.IsGame(other, reader));
    }

    [Fact]
    public void ABrokenShortcut_HasNoTarget()
    {
        using var tree = new TempTree();
        string broken = tree.AddFile("broken.lnk", 10);

        Assert.Null(new ShellShortcutReader().TargetOf(broken));
    }

    [Fact]
    public void AMissingShortcut_HasNoTarget()
    {
        using var tree = new TempTree();

        Assert.Null(new ShellShortcutReader().TargetOf(tree.PathOf("gone.lnk")));
    }
}
