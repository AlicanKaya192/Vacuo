namespace Vacuo.Core.Scanning;

/// <summary>The caches a browser fills again by itself as pages are visited.</summary>
public enum BrowserCacheKind
{
    PageCache,
    CodeCache,
    GpuCache,
}

/// <summary>Where a browser keeps its profiles inside its data folder.</summary>
public enum BrowserProfileLayout
{
    /// <summary><c>Default</c> and <c>Profile N</c> folders, each with a <c>Preferences</c> file.</summary>
    ChromiumProfiles,

    /// <summary>The data folder itself is the only profile, as in Opera.</summary>
    ChromiumSingleProfile,

    /// <summary>Every folder under <c>Profiles</c>; only their <c>cache2</c> is listed.</summary>
    FirefoxProfiles,
}

/// <summary>
/// One browser: the folder under <see cref="Root"/> that holds its profiles, and the names its
/// processes run under, which tell whether it is open.
/// </summary>
public sealed record BrowserDefinition(
    string Id, BrowserProfileLayout Layout, KnownRoot Root, string DataFolder, IReadOnlyList<string> ProcessNames);

/// <summary>
/// One cache folder inside every profile of a browser. Cookies, history, passwords, extensions
/// and the sites' offline data (<c>Service Worker</c>, <c>IndexedDB</c>) never match.
/// </summary>
public sealed record BrowserCacheRule(
    string Id, Bucket Bucket, RebuildCost Cost, BrowserDefinition Browser, BrowserCacheKind Kind, string RelativePath)
    : CacheRule(Id, Bucket, Cost);

/// <summary>
/// The browsers Vacuo knows, each on its own so the interface can show them apart. Browser
/// caches are only ever moved by the user's choice; Otomatik Bakım does not scan them.
/// </summary>
public static class BrowserCacheRules
{
    public static readonly IReadOnlyList<BrowserDefinition> Browsers =
    [
        // Seen on the development machine.
        Chromium("edge", @"Microsoft\Edge\User Data", "msedge"),
        Chromium("brave", @"BraveSoftware\Brave-Browser\User Data", "brave"),
        Chromium("chrome", @"Google\Chrome\User Data", "chrome"),
        // The browsers' documented defaults; not yet seen on the development machine.
        Chromium("edge-beta", @"Microsoft\Edge Beta\User Data", "msedge"),
        Chromium("edge-dev", @"Microsoft\Edge Dev\User Data", "msedge"),
        Chromium("edge-canary", @"Microsoft\Edge SxS\User Data", "msedge"),
        Chromium("brave-beta", @"BraveSoftware\Brave-Browser-Beta\User Data", "brave"),
        Chromium("brave-nightly", @"BraveSoftware\Brave-Browser-Nightly\User Data", "brave"),
        Chromium("chrome-beta", @"Google\Chrome Beta\User Data", "chrome"),
        Chromium("chrome-dev", @"Google\Chrome Dev\User Data", "chrome"),
        Chromium("chrome-canary", @"Google\Chrome SxS\User Data", "chrome"),
        Chromium("chromium", @"Chromium\User Data", "chrome"),
        Chromium("vivaldi", @"Vivaldi\User Data", "vivaldi"),
        Chromium("yandex", @"Yandex\YandexBrowser\User Data", "browser"),
        new("opera", BrowserProfileLayout.ChromiumSingleProfile, KnownRoot.LocalAppData, @"Opera Software\Opera Stable", ["opera"]),
        new("opera-gx", BrowserProfileLayout.ChromiumSingleProfile, KnownRoot.LocalAppData, @"Opera Software\Opera GX Stable", ["opera"]),
        Firefox("firefox", @"Mozilla\Firefox\Profiles", "firefox"),
        Firefox("librewolf", @"librewolf\Profiles", "librewolf"),
        Firefox("waterfox", @"Waterfox\Profiles", "waterfox"),
        Firefox("floorp", @"Floorp\Profiles", "floorp"),
        Firefox("zen", @"zen\Profiles", "zen"),
    ];

    private static readonly (BrowserCacheKind Kind, string RelativePath)[] ChromiumCaches =
    [
        (BrowserCacheKind.PageCache, @"Cache\Cache_Data"),
        (BrowserCacheKind.CodeCache, "Code Cache"),
        (BrowserCacheKind.GpuCache, "GPUCache"),
    ];

    private static readonly (BrowserCacheKind Kind, string RelativePath)[] FirefoxCaches =
    [
        (BrowserCacheKind.PageCache, "cache2"),
    ];

    /// <summary>The cache folders looked for in each profile of <paramref name="browser"/>.</summary>
    public static IReadOnlyList<BrowserCacheRule> RulesFor(BrowserDefinition browser)
    {
        var caches = browser.Layout == BrowserProfileLayout.FirefoxProfiles ? FirefoxCaches : ChromiumCaches;
        return caches
            .Select(cache => new BrowserCacheRule(
                $"browser-{browser.Id}-{KindId(cache.Kind)}", Bucket.RebuildsItself, RebuildCost.Redownloaded,
                browser, cache.Kind, cache.RelativePath))
            .ToList();
    }

    /// <summary>
    /// A browser cache is locked while its browser runs, since the browser keeps writing to it.
    /// Other findings are never locked here.
    /// </summary>
    public static bool IsLocked(ScanFinding finding, IReadOnlySet<string> runningProcessNames) =>
        finding.Rule is BrowserCacheRule rule && rule.Browser.ProcessNames.Any(runningProcessNames.Contains);

    private static string KindId(BrowserCacheKind kind) => kind switch
    {
        BrowserCacheKind.PageCache => "page-cache",
        BrowserCacheKind.CodeCache => "code-cache",
        BrowserCacheKind.GpuCache => "gpu-cache",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static BrowserDefinition Chromium(string id, string dataFolder, string processName) =>
        new(id, BrowserProfileLayout.ChromiumProfiles, KnownRoot.LocalAppData, dataFolder, [processName]);

    private static BrowserDefinition Firefox(string id, string dataFolder, string processName) =>
        new(id, BrowserProfileLayout.FirefoxProfiles, KnownRoot.LocalAppData, dataFolder, [processName]);
}
