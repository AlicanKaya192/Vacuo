using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;
using Vacuo.Core.Scanning;
using Vacuo.Core.Tests.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Disk;

public class BrowserCacheScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlySet<string> NothingRuns = new HashSet<string>();
    private static readonly IReadOnlySet<string> EdgeRuns = new HashSet<string>(["msedge"], StringComparer.OrdinalIgnoreCase);

    private sealed class Setup : IDisposable
    {
        public Setup()
        {
            var time = new FixedTimeProvider(Now);
            Bin = new FakeRecycleBin(Tree.PathOf("bin"), time);
            Journal = new ActionJournal(Tree.PathOf("journal.jsonl"));
            Runner = new ActionRunner(Journal, Bin, time);
        }

        public TempTree Tree { get; } = new();
        public FakeRecycleBin Bin { get; }
        public ActionJournal Journal { get; }
        public ActionRunner Runner { get; }

        // A cache folder of a browser's profile, with one file of the given size in it.
        public ScanFinding Cache(string browserId, string profile, BrowserCacheKind kind, int sizeBytes)
        {
            BrowserDefinition browser = BrowserCacheRules.Browsers.Single(candidate => candidate.Id == browserId);
            BrowserCacheRule rule = BrowserCacheRules.RulesFor(browser).Single(candidate => candidate.Kind == kind);
            string folder = Path.Combine(browserId, profile, rule.RelativePath);
            Tree.AddFile(Path.Combine(folder, "data"), sizeBytes);
            return new ScanFinding(Tree.PathOf(folder), rule, new DirectorySize(sizeBytes, 0), DateTime.UnixEpoch);
        }

        public void Dispose() => Tree.Dispose();
    }

    private static BrowserCacheScreen ScreenOf(params ScanFinding[] findings) =>
        new(ScanSummary.Combine([new ScanResult(findings, SkippedEntries: 0)]));

    [Fact]
    public void Groups_OneGroupPerBrowserLargestFirst()
    {
        using var setup = new Setup();
        ScanFinding edgePage = setup.Cache("edge", "Default", BrowserCacheKind.PageCache, 300);
        ScanFinding edgeGpu = setup.Cache("edge", "Default", BrowserCacheKind.GpuCache, 100);
        ScanFinding brave = setup.Cache("brave", "Default", BrowserCacheKind.CodeCache, 700);

        IReadOnlyList<BrowserCacheGroup> groups = ScreenOf(edgePage, edgeGpu, brave).Groups(NothingRuns);

        Assert.Equal(["brave", "edge"], groups.Select(group => group.Browser.Id));
        Assert.Equal([700L, 400L], groups.Select(group => group.TotalBytes));
        Assert.Equal([edgePage.Path, edgeGpu.Path], groups[1].Items.Select(item => item.Path));
    }

    [Fact]
    public void Groups_RunningBrowser_IsLockedAndOthersAreNot()
    {
        using var setup = new Setup();
        ScanFinding edge = setup.Cache("edge", "Default", BrowserCacheKind.PageCache, 300);
        ScanFinding brave = setup.Cache("brave", "Default", BrowserCacheKind.PageCache, 200);

        IReadOnlyList<BrowserCacheGroup> groups = ScreenOf(edge, brave).Groups(EdgeRuns);

        Assert.True(groups.Single(group => group.Browser.Id == "edge").IsLocked);
        Assert.False(groups.Single(group => group.Browser.Id == "brave").IsLocked);
    }

    [Fact]
    public void Select_CacheOfARunningBrowser_IsRefused()
    {
        using var setup = new Setup();
        ScanFinding edge = setup.Cache("edge", "Default", BrowserCacheKind.PageCache, 300);
        BrowserCacheScreen screen = ScreenOf(edge);

        bool selected = screen.Select(edge.Path, EdgeRuns);

        Assert.False(selected);
        Assert.Equal(0, screen.SelectedCount);
    }

    [Fact]
    public void MoveSelected_ClosedBrowser_GoesToTheRecycleBinAndTheJournal()
    {
        using var setup = new Setup();
        ScanFinding edge = setup.Cache("edge", "Default", BrowserCacheKind.PageCache, 300);
        BrowserCacheScreen screen = ScreenOf(edge);
        screen.Select(edge.Path, NothingRuns);

        MoveOutcome outcome = screen.MoveSelected(NothingRuns, setup.Runner, setup.Bin);

        JournalEntry entry = Assert.Single(outcome.Moved);
        Assert.Equal(300, entry.SizeBytes);
        Assert.False(Directory.Exists(edge.Path));
        Assert.Single(setup.Journal.ReadAll());
        Assert.Empty(screen.Groups(NothingRuns));
    }

    [Fact]
    public void MoveSelected_BrowserOpenedAfterSelecting_LeavesItsCachesInPlace()
    {
        using var setup = new Setup();
        ScanFinding edge = setup.Cache("edge", "Default", BrowserCacheKind.PageCache, 300);
        ScanFinding brave = setup.Cache("brave", "Default", BrowserCacheKind.PageCache, 200);
        BrowserCacheScreen screen = ScreenOf(edge, brave);
        screen.Select(edge.Path, NothingRuns);
        screen.Select(brave.Path, NothingRuns);

        MoveOutcome outcome = screen.MoveSelected(EdgeRuns, setup.Runner, setup.Bin);

        Assert.Single(outcome.Moved);
        Assert.False(Directory.Exists(brave.Path));
        Assert.True(Directory.Exists(edge.Path));
        Assert.False(screen.IsSelected(edge.Path));
    }

    [Theory]
    [InlineData("edge", "Default", BrowserCacheKind.PageCache)]
    [InlineData("brave", "Profile 2", BrowserCacheKind.CodeCache)]
    [InlineData("firefox", "abcd.default-release", BrowserCacheKind.PageCache)]
    public void ProfileOf_IsTheProfileFolderAboveTheCache(string browserId, string profile, BrowserCacheKind kind)
    {
        using var setup = new Setup();
        ScanFinding cache = setup.Cache(browserId, profile, kind, 10);

        Assert.Equal(profile, BrowserCacheScreen.ProfileOf(cache));
    }
}
