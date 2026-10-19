using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Scanning;

public class UserFileScannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static IReadOnlyList<ScanFinding> Scan(TempTree tree, string? pictures = null) =>
        new UserFileScanner(new KnownRoots(tree.PathOf("Local"), tree.PathOf("Home"), pictures), new FixedClock(Now)).Scan().Findings;

    private static string AddFileAgedDays(TempTree tree, string relativePath, int sizeBytes, int ageDays)
    {
        string path = tree.AddFile(relativePath, sizeBytes);
        File.SetLastWriteTimeUtc(path, Now.UtcDateTime.AddDays(-ageDays));
        return path;
    }

    [Fact]
    public void Scan_Screenshot_IsSuggestedInTheYoursBucket()
    {
        using var tree = new TempTree();
        string screenshot = AddFileAgedDays(tree, @"Home\Pictures\Screenshots\Screenshot 2026-10-01.png", 800, ageDays: 1);

        ScanFinding finding = Assert.Single(Scan(tree));

        Assert.Equal("screenshots", finding.Rule.Id);
        Assert.Equal(Bucket.Yours, finding.Rule.Bucket);
        Assert.Equal(RebuildCost.CannotBeRebuilt, finding.Rule.Cost);
        Assert.Equal(screenshot, finding.Path);
        Assert.Equal(800, finding.Size.Bytes);
    }

    [Fact]
    public void Scan_OtherFilesInScreenshots_AreIgnored()
    {
        using var tree = new TempTree();
        AddFileAgedDays(tree, @"Home\Pictures\Screenshots\notes.txt", 10, ageDays: 1);
        AddFileAgedDays(tree, @"Home\Pictures\holiday.jpg", 10, ageDays: 1);

        Assert.Empty(Scan(tree));
    }

    [Fact]
    public void Scan_ScreenshotsInRedirectedPicturesFolder_AreFound()
    {
        using var tree = new TempTree();
        AddFileAgedDays(tree, @"OneDrive\Pictures\Screenshots\shot.jpg", 50, ageDays: 1);

        ScanFinding finding = Assert.Single(Scan(tree, pictures: tree.PathOf(@"OneDrive\Pictures")));

        Assert.Equal("screenshots", finding.Rule.Id);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("tool.msi")]
    [InlineData("app.msix")]
    [InlineData("game.appx")]
    [InlineData("SETUP.EXE")]
    public void Scan_InstallerOlderThan30Days_IsSuggested(string installer)
    {
        using var tree = new TempTree();
        AddFileAgedDays(tree, $@"Home\Downloads\{installer}", 900, ageDays: 31);

        ScanFinding finding = Assert.Single(Scan(tree));

        Assert.Equal("old-installers", finding.Rule.Id);
        Assert.Equal(Bucket.Yours, finding.Rule.Bucket);
    }

    [Fact]
    public void Scan_RecentInstaller_IsIgnored()
    {
        using var tree = new TempTree();
        AddFileAgedDays(tree, @"Home\Downloads\setup.exe", 900, ageDays: 10);

        Assert.Empty(Scan(tree));
    }

    [Fact]
    public void Scan_OldDownloadThatIsNotAnInstaller_IsIgnored()
    {
        using var tree = new TempTree();
        AddFileAgedDays(tree, @"Home\Downloads\thesis.pdf", 900, ageDays: 400);

        Assert.Empty(Scan(tree));
    }

    [Fact]
    public void Scan_InstallerInASubfolderOfDownloads_IsIgnored()
    {
        using var tree = new TempTree();
        AddFileAgedDays(tree, @"Home\Downloads\project\tools\setup.exe", 900, ageDays: 400);

        Assert.Empty(Scan(tree));
    }

    [Fact]
    public void Scan_ScreenshotsFolderThatIsAJunction_IsNotFollowed()
    {
        using var tree = new TempTree();
        AddFileAgedDays(tree, @"Elsewhere\shot.png", 50, ageDays: 1);
        tree.AddDirectory(@"Home\Pictures");
        tree.AddJunction(@"Home\Pictures\Screenshots", "Elsewhere");

        Assert.Empty(Scan(tree));
    }

    [Fact]
    public void Scan_NoUserFolders_FindsNothing()
    {
        using var tree = new TempTree();

        Assert.Empty(Scan(tree));
    }

    [Fact]
    public void RuleIds_DoNotCollideWithCacheRules()
    {
        string[] ids = KnownCacheRules.All.Concat(UserFileRules.All).Select(rule => rule.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }
}
