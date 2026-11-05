using Vacuo.Core.Actions;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Disk;

/// <summary>One browser's caches on the Tarayıcılar screen; locked while that browser is open.</summary>
public sealed record BrowserCacheGroup(BrowserDefinition Browser, IReadOnlyList<ScanFinding> Items, long TotalBytes, bool IsLocked);

/// <summary>
/// The Tarayıcılar screen: the browsers' caches, grouped by browser, moved to the Recycle Bin like the
/// Disk screen's rows (so the Journal can put them back). A cache of an open browser can be neither
/// chosen nor moved; which browsers are open is read again right before moving.
/// </summary>
public sealed class BrowserCacheScreen(ScanSummary summary)
{
    private readonly DiskScreen _disk = new(summary);

    public int SelectedCount => _disk.SelectedCount;

    public long SelectedBytes => _disk.SelectedBytes;

    public bool IsSelected(string path) => _disk.IsSelected(path);

    /// <summary>The browsers with the most cache first; in each, profile by profile.</summary>
    public IReadOnlyList<BrowserCacheGroup> Groups(IReadOnlySet<string> runningProcessNames) => _disk.Buckets
        .SelectMany(bucket => bucket.Items)
        .GroupBy(finding => RuleOf(finding).Browser)
        .Select(group => new BrowserCacheGroup(
            group.Key,
            group.OrderBy(ProfileOf, StringComparer.OrdinalIgnoreCase).ThenBy(finding => RuleOf(finding).Kind).ToList(),
            group.Sum(finding => finding.Size.Bytes),
            BrowserCacheRules.IsLocked(group.First(), runningProcessNames)))
        .OrderByDescending(group => group.TotalBytes)
        .ToList();

    /// <summary>Chooses a cache unless its browser is open; says whether it was chosen.</summary>
    public bool Select(string path, IReadOnlySet<string> runningProcessNames)
    {
        if (_disk.Buckets.SelectMany(bucket => bucket.Items).Any(finding =>
                string.Equals(finding.Path, path, StringComparison.OrdinalIgnoreCase) && BrowserCacheRules.IsLocked(finding, runningProcessNames)))
        {
            return false;
        }

        _disk.Select(path);
        return true;
    }

    public void Deselect(string path) => _disk.Deselect(path);

    public IReadOnlyList<ScanFinding> Preview() => _disk.Preview();

    /// <summary>
    /// Leaves out the caches of browsers opened since they were chosen, then moves the rest to the
    /// Recycle Bin. A left-out cache is no longer chosen, so the screen shows it as locked.
    /// </summary>
    public MoveOutcome MoveSelected(
        IReadOnlySet<string> runningProcessNames, ActionRunner runner, IRecycleBin recycleBin, IProgress<MoveProgress>? progress = null)
    {
        foreach (ScanFinding finding in Preview().Where(finding => BrowserCacheRules.IsLocked(finding, runningProcessNames)))
        {
            _disk.Deselect(finding.Path);
        }

        return _disk.MoveSelected(runner, recycleBin, progress);
    }

    /// <summary>The profile's folder name, such as <c>Default</c> or <c>Profile 2</c>.</summary>
    public static string ProfileOf(ScanFinding finding)
    {
        int depth = RuleOf(finding).RelativePath.Split(Path.DirectorySeparatorChar).Length;
        DirectoryInfo? profile = new DirectoryInfo(finding.Path);
        for (int i = 0; i < depth && profile is not null; i++)
        {
            profile = profile.Parent;
        }

        return profile?.Name ?? string.Empty;
    }

    private static BrowserCacheRule RuleOf(ScanFinding finding) =>
        finding.Rule as BrowserCacheRule
        ?? throw new ArgumentException($"'{finding.Path}' is not a browser cache.", nameof(finding));
}
