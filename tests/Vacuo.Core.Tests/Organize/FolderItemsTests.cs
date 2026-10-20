using Vacuo.Core.Organize;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Organize;

public class FolderItemsTests
{
    [Fact]
    public void OnlyTopLevelFiles_AreRead()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Desktop\a.pdf", 10);
        tree.AddFile(@"Desktop\Photos\b.png", 10);

        IReadOnlyList<FolderItem> items = FolderItems.Read(tree.PathOf("Desktop"));

        Assert.Equal(["a.pdf"], items.Select(item => item.Name));
    }

    [Theory]
    [InlineData("Steam.lnk")]
    [InlineData("site.url")]
    [InlineData("desktop.ini")]
    public void ShortcutsAndDesktopIni_AreLeftAlone(string name)
    {
        using var tree = new TempTree();
        tree.AddFile($@"Desktop\{name}", 10);

        Assert.Empty(FolderItems.Read(tree.PathOf("Desktop")));
    }

    [Fact]
    public void HiddenFiles_AreLeftAlone()
    {
        using var tree = new TempTree();
        string hidden = tree.AddFile(@"Desktop\secret.txt", 10);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Assert.Empty(FolderItems.Read(tree.PathOf("Desktop")));
    }

    [Fact]
    public void LastChange_AndDownloadSource_AreRead()
    {
        using var tree = new TempTree();
        string file = tree.AddFile(@"Downloads\repo.zip", 10);
        File.WriteAllText(file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://github.com/x/repo.zip\r\n");
        // Writing the note counts as a change, so the time is set after it.
        var changed = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Local);
        File.SetLastWriteTime(file, changed);

        FolderItem item = Assert.Single(FolderItems.Read(tree.PathOf("Downloads")));

        Assert.Equal(changed, item.LastWrite.LocalDateTime);
        Assert.Equal("github.com", item.SourceHost);
    }

    [Fact]
    public void SystemFolder_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => FolderItems.Read(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
    }
}
