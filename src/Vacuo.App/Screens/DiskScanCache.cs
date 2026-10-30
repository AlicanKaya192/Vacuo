using Vacuo.Core.Disk;
using Vacuo.Core.Notices;
using Vacuo.Core.Scanning;

namespace Vacuo.App.Screens;

/// <summary>
/// Runs the disk scan off the UI thread and keeps the latest result for a few minutes, so
/// reopening the panel does not rescan. The categories are measured alongside, also off the UI
/// thread; they take far longer (~26 s on this machine), so they finish on their own. Used from
/// the UI thread only.
/// </summary>
internal sealed class DiskScanCache(TimeProvider time)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private Task<ScanSummary>? _running;
    private Task<(IReadOnlyList<CategorySize> Sizes, DiskGrowth? Growth)>? _measuring;
    private DateTimeOffset _scannedAt = DateTimeOffset.MinValue;

    public ScanSummary? Latest { get; private set; }

    public IReadOnlyList<CategorySize>? CategorySizes { get; private set; }

    /// <summary>From the disk history; null until two different days can be compared.</summary>
    public DiskGrowth? Growth { get; private set; }

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

    /// <summary>
    /// Starts measuring the categories, or joins the measurement already running. The folders are only read;
    /// the only thing written is the reading, to the disk history.
    /// </summary>
    public async Task MeasureCategoriesAsync()
    {
        _measuring ??= Task.Run(MeasureAndRecord);
        try
        {
            (CategorySizes, Growth) = await _measuring;
        }
        finally
        {
            _measuring = null;
        }
    }

    /// <summary>The used space by category with the latest scan's flows; null until both are in.</summary>
    public DiskCategories? Categories(long usedBytes) =>
        Latest is not null && CategorySizes is not null ? DiskCategories.From(CategorySizes, Latest, usedBytes) : null;

    private (IReadOnlyList<CategorySize> Sizes, DiskGrowth? Growth) MeasureAndRecord()
    {
        IReadOnlyList<CategorySize> sizes = CategorySizer.Measure(CategoryRoots.ForThisMachine());
        DateOnly today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var store = new DiskHistoryStore(DiskHistoryStore.DefaultPath);
        DiskHistory history = SystemDrive.Read() is { } drive
            ? store.Record(DiskSnapshot.From(today, drive.TotalSize - drive.AvailableFreeSpace, sizes))
            : store.Load();
        return (sizes, DiskGrowth.From(history, today));
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
