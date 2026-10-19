using Vacuo.Core.Scanning;

namespace Vacuo.App.Screens;

/// <summary>
/// Runs the disk scan off the UI thread and keeps the latest result for a few minutes, so
/// reopening the panel does not rescan. Used from the UI thread only.
/// </summary>
internal sealed class DiskScanCache(TimeProvider time)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private Task<ScanSummary>? _running;
    private DateTimeOffset _scannedAt = DateTimeOffset.MinValue;

    public ScanSummary? Latest { get; private set; }

    public bool IsStale => Latest is null || time.GetUtcNow() - _scannedAt > MaxAge;

    /// <summary>Drops items that are gone (moved to the Recycle Bin) without scanning again.</summary>
    public void Forget(IReadOnlyList<string> paths)
    {
        if (Latest is not null)
        {
            Latest = Latest.Without(paths);
        }
    }

    /// <summary>Starts a scan, or joins the one already running.</summary>
    public async Task<ScanSummary> ScanAsync()
    {
        _running ??= Task.Run(ScanUserFolders);
        try
        {
            ScanSummary summary = await _running;
            Latest = summary;
            _scannedAt = time.GetUtcNow();
            return summary;
        }
        finally
        {
            _running = null;
        }
    }

    private static ScanSummary ScanUserFolders()
    {
        KnownRoots roots = KnownRoots.ForCurrentUser();
        return ScanSummary.Combine(
        [
            new KnownLocationScanner(roots).Scan(),
            new ProjectArtifactScanner([roots.UserProfile]).Scan(),
            new UserFileScanner(roots, TimeProvider.System).Scan(),
        ]);
    }
}
