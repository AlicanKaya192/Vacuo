using Vacuo.Core.Actions;
using Vacuo.Core.Tests.Scanning;
using Vacuo.Windows;

namespace Vacuo.Windows.Tests;

/// <summary>Runs the real shell call on a throwaway folder under the temp folder.</summary>
public class FolderIconWriterTests
{
    private const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;

    [Fact]
    public void Apply_CopiesTheIconInAndPointsTheFolderAtIt()
    {
        using var tree = new TempTree();
        string iconFile = tree.AddFile("images.ico", 64);
        string folder = tree.AddDirectory("Pictures");

        new FolderIconWriter().Apply(folder, iconFile);

        string icon = Path.Combine(folder, FolderIcon.IconFile);
        string desktopIni = Path.Combine(folder, FolderIcon.DesktopIni);
        Assert.Equal(File.ReadAllBytes(iconFile), File.ReadAllBytes(icon));
        Assert.Equal(HiddenSystem, File.GetAttributes(icon) & HiddenSystem);
        Assert.Equal(HiddenSystem, File.GetAttributes(desktopIni) & HiddenSystem);
        Assert.Contains("IconResource=folder.ico,0", File.ReadAllText(desktopIni));
        Assert.True(new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void Apply_FolderIcoAlreadyThere_ThrowsAndLeavesItAlone()
    {
        using var tree = new TempTree();
        string iconFile = tree.AddFile("images.ico", 64);
        string usersOwn = tree.AddFile(@"Pictures\folder.ico", 20);

        Assert.ThrowsAny<IOException>(() => new FolderIconWriter().Apply(tree.PathOf("Pictures"), iconFile));

        Assert.Equal(20, new FileInfo(usersOwn).Length);
        Assert.False(File.Exists(tree.PathOf(@"Pictures\desktop.ini")));
    }
}
