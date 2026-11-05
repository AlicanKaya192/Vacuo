using Vacuo.Core.Maintenance;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Scanning;

public class BrowserCacheScannerTests
{
    private const string EdgeProfile = @"Local\Microsoft\Edge\User Data\Default";

    private static BrowserCacheScanner ScannerFor(TempTree tree) =>
        new(new KnownRoots(LocalAppData: tree.PathOf("Local"), UserProfile: tree.PathOf("Home")));

    private static void AddChromiumProfile(TempTree tree, string profile)
    {
        tree.AddFile(profile + @"\Preferences", 10);
    }

    [Fact]
    public void Scan_ChromiumProfile_ListsOnlyTheThreeCacheFolders()
    {
        using var tree = new TempTree();
        AddChromiumProfile(tree, EdgeProfile);
        tree.AddFile(EdgeProfile + @"\Cache\Cache_Data\data_1", 300);
        tree.AddFile(EdgeProfile + @"\Code Cache\js\index", 200);
        tree.AddFile(EdgeProfile + @"\GPUCache\data_0", 100);
        tree.AddFile(EdgeProfile + @"\Network\Cookies", 50);
        tree.AddFile(EdgeProfile + @"\Service Worker\CacheStorage\x", 50);
        tree.AddFile(EdgeProfile + @"\IndexedDB\site\db", 50);
        tree.AddFile(EdgeProfile + @"\Login Data", 50);

        IReadOnlyList<ScanFinding> findings = ScannerFor(tree).Scan().Findings;

        Assert.Equal(
            new[] { @"Cache\Cache_Data", "Code Cache", "GPUCache" }.Select(folder => tree.PathOf(EdgeProfile + "\\" + folder)).Order(),
            findings.Select(finding => finding.Path).Order());
        Assert.Equal(600, findings.Sum(finding => finding.Size.Bytes));
    }

    [Fact]
    public void Scan_BrowserCache_IsInRebuildsItselfBucketAndRedownloaded()
    {
        using var tree = new TempTree();
        AddChromiumProfile(tree, EdgeProfile);
        tree.AddFile(EdgeProfile + @"\GPUCache\data_0", 100);

        ScanFinding finding = Assert.Single(ScannerFor(tree).Scan().Findings);

        var rule = Assert.IsType<BrowserCacheRule>(finding.Rule);
        Assert.Equal("edge", rule.Browser.Id);
        Assert.Equal(BrowserCacheKind.GpuCache, rule.Kind);
        Assert.Equal(Bucket.RebuildsItself, rule.Bucket);
        Assert.Equal(RebuildCost.Redownloaded, rule.Cost);
    }

    [Fact]
    public void Scan_EveryChromiumProfile_IsFoundSeparately()
    {
        using var tree = new TempTree();
        const string braveData = @"Local\BraveSoftware\Brave-Browser\User Data";
        AddChromiumProfile(tree, braveData + @"\Default");
        AddChromiumProfile(tree, braveData + @"\Profile 2");
        tree.AddFile(braveData + @"\Default\GPUCache\a", 10);
        tree.AddFile(braveData + @"\Profile 2\GPUCache\b", 20);

        IReadOnlyList<ScanFinding> findings = ScannerFor(tree).Scan().Findings;

        Assert.Equal(2, findings.Count);
        Assert.All(findings, finding => Assert.Equal("brave", ((BrowserCacheRule)finding.Rule).Browser.Id));
    }

    [Fact]
    public void Scan_FolderWithoutPreferences_IsNotTakenForAProfile()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Local\Google\Chrome\User Data\Default\GPUCache\a", 10);
        tree.AddFile(@"Local\Google\Chrome\User Data\Profile 1\Cache\Cache_Data\b", 10);

        Assert.Empty(ScannerFor(tree).Scan().Findings);
    }

    [Fact]
    public void Scan_FolderNotNamedLikeAProfile_IsSkipped()
    {
        using var tree = new TempTree();
        const string other = @"Local\Google\Chrome\User Data\Crashpad";
        AddChromiumProfile(tree, other);
        tree.AddFile(other + @"\GPUCache\a", 10);

        Assert.Empty(ScannerFor(tree).Scan().Findings);
    }

    [Fact]
    public void Scan_FirefoxProfiles_ListOnlyCache2()
    {
        using var tree = new TempTree();
        const string profile = @"Local\Mozilla\Firefox\Profiles\abcd1234.default-release";
        tree.AddFile(profile + @"\cache2\entries\e1", 500);
        tree.AddFile(profile + @"\startupCache\scriptCache.bin", 40);
        tree.AddFile(profile + @"\thumbnails\t.png", 40);

        ScanFinding finding = Assert.Single(ScannerFor(tree).Scan().Findings);

        Assert.Equal(tree.PathOf(profile + @"\cache2"), finding.Path);
        Assert.Equal("firefox", ((BrowserCacheRule)finding.Rule).Browser.Id);
        Assert.Equal(BrowserCacheKind.PageCache, ((BrowserCacheRule)finding.Rule).Kind);
    }

    [Fact]
    public void Scan_OperaWithItsProfileAtTheTop_IsFound()
    {
        using var tree = new TempTree();
        tree.AddFile(@"Local\Opera Software\Opera Stable\Cache\Cache_Data\d", 70);

        ScanFinding finding = Assert.Single(ScannerFor(tree).Scan().Findings);

        Assert.Equal("opera", ((BrowserCacheRule)finding.Rule).Browser.Id);
    }

    [Fact]
    public void Scan_SeveralBrowsers_AreEachFoundWithTheirOwnRule()
    {
        using var tree = new TempTree();
        AddChromiumProfile(tree, EdgeProfile);
        tree.AddFile(EdgeProfile + @"\GPUCache\a", 10);
        AddChromiumProfile(tree, @"Local\Vivaldi\User Data\Default");
        tree.AddFile(@"Local\Vivaldi\User Data\Default\GPUCache\a", 10);
        tree.AddFile(@"Local\Mozilla\Firefox\Profiles\x.default\cache2\a", 10);

        string[] browsers = ScannerFor(tree).Scan().Findings
            .Select(finding => ((BrowserCacheRule)finding.Rule).Browser.Id)
            .Order()
            .ToArray();

        Assert.Equal(["edge", "firefox", "vivaldi"], browsers);
    }

    [Fact]
    public void Scan_NoBrowsers_FindsNothing()
    {
        using var tree = new TempTree();
        tree.AddDirectory("Local");

        ScanResult result = ScannerFor(tree).Scan();

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.SkippedEntries);
    }

    [Fact]
    public void Scan_CacheFolderThatIsAJunction_IsNotFollowed()
    {
        using var tree = new TempTree();
        AddChromiumProfile(tree, EdgeProfile);
        tree.AddFile(@"Elsewhere\gpu\a", 400);
        tree.AddJunction(EdgeProfile + @"\GPUCache", "Elsewhere\\gpu");

        Assert.Empty(ScannerFor(tree).Scan().Findings);
    }

    [Fact]
    public void Scan_CacheReachedThroughAJunctionedParent_IsNotFollowed()
    {
        using var tree = new TempTree();
        AddChromiumProfile(tree, EdgeProfile);
        tree.AddFile(@"Elsewhere\cache\Cache_Data\a", 400);
        tree.AddJunction(EdgeProfile + @"\Cache", "Elsewhere\\cache");

        Assert.Empty(ScannerFor(tree).Scan().Findings);
    }

    [Fact]
    public void Scan_ProfileThatIsAJunction_IsNotFollowed()
    {
        using var tree = new TempTree();
        AddChromiumProfile(tree, @"Elsewhere\profile");
        tree.AddFile(@"Elsewhere\profile\GPUCache\a", 400);
        tree.AddDirectory(@"Local\Microsoft\Edge\User Data");
        tree.AddJunction(EdgeProfile, "Elsewhere\\profile");

        Assert.Empty(ScannerFor(tree).Scan().Findings);
    }

    [Fact]
    public void IsLocked_BrowserRunning_LocksItsCachesOnly()
    {
        using var tree = new TempTree();
        AddChromiumProfile(tree, EdgeProfile);
        tree.AddFile(EdgeProfile + @"\GPUCache\a", 10);
        tree.AddFile(@"Local\Mozilla\Firefox\Profiles\x.default\cache2\a", 10);
        IReadOnlyList<ScanFinding> findings = ScannerFor(tree).Scan().Findings;
        var running = new HashSet<string>(["MSEDGE", "explorer"], StringComparer.OrdinalIgnoreCase);

        ScanFinding edge = findings.Single(finding => ((BrowserCacheRule)finding.Rule).Browser.Id == "edge");
        ScanFinding firefox = findings.Single(finding => ((BrowserCacheRule)finding.Rule).Browser.Id == "firefox");

        Assert.True(BrowserCacheRules.IsLocked(edge, running));
        Assert.False(BrowserCacheRules.IsLocked(firefox, running));
    }

    [Fact]
    public void IsLocked_FindingOfAnotherRule_IsNeverLocked()
    {
        var pip = new ScanFinding(@"C:\x", KnownCacheRules.KnownLocations[0], new DirectorySize(1, 0), DateTime.UtcNow);

        Assert.False(BrowserCacheRules.IsLocked(pip, new HashSet<string>(["pip"])));
    }

    [Fact]
    public void Browsers_HaveUniqueIdsAndRelativeFoldersInsideTheirRoot()
    {
        string[] ids = BrowserCacheRules.Browsers.Select(browser => browser.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(BrowserCacheRules.Browsers, browser =>
        {
            Assert.False(Path.IsPathRooted(browser.DataFolder), browser.Id);
            Assert.DoesNotContain("..", browser.DataFolder.Split('\\'));
            Assert.NotEmpty(browser.ProcessNames);
        });
    }

    [Fact]
    public void Rules_AreNeverPartOfAutomaticMaintenance()
    {
        IEnumerable<string> ruleIds = BrowserCacheRules.Browsers
            .SelectMany(BrowserCacheRules.RulesFor)
            .Select(rule => rule.Id);

        Assert.All(ruleIds, id =>
        {
            Assert.DoesNotContain(id, MaintenanceSelection.DownloadCacheRules);
            Assert.DoesNotContain(id, MaintenanceSelection.ProjectLeftoverRules);
        });
    }
}
