namespace Vacuo.Core.Ram;

/// <summary>One group on the Memory screen: the apps big enough to list, the rest summed as "others".</summary>
public sealed record MemorySection(AppGroup Group, IReadOnlyList<AppUse> Shown, int OthersCount, long OthersBytes, long TotalBytes);

/// <summary>
/// The Memory screen without its looks: the pressure and the running apps in three groups, always
/// in the order idle, in use, protected. Without the tray's foreground history (the panel ran on
/// its own) nobody knows how long an app has been unused, so nothing is called idle.
/// </summary>
public sealed record MemoryScreen(
    MemorySnapshot Snapshot, MemoryPressure Pressure, DateTimeOffset? WatchingSince, IReadOnlyList<MemorySection> Sections)
{
    /// <summary>Smaller apps are many and say little one by one; they are summed instead.</summary>
    public const long ListedFromBytes = 50L << 20;

    public bool HistoryKnown => WatchingSince is not null;

    /// <summary>Idle and in-use apps may be picked to close; system and protected ones never (B-153).</summary>
    public static bool CanClose(AppUse use) => use.Group != AppGroup.Protected;

    private static readonly AppGroup[] Order = [AppGroup.Idle, AppGroup.InUse, AppGroup.Protected];

    public static MemoryScreen From(MemorySnapshot snapshot, IEnumerable<RunningApp> apps, ForegroundHistory? history, DateTimeOffset now)
    {
        IReadOnlyList<AppUse> sorted = AppSorter.Sort(apps, history, now);
        List<MemorySection> sections = Order
            .Select(group => SectionOf(group, sorted.Where(use => use.Group == group).ToList()))
            .ToList();

        return new MemoryScreen(snapshot, MemoryPressureRule.Of(snapshot), history?.StartedAt, sections);
    }

    private static MemorySection SectionOf(AppGroup group, List<AppUse> uses)
    {
        List<AppUse> shown = uses.Where(use => use.App.PrivateBytes >= ListedFromBytes).ToList();
        List<AppUse> others = uses.Where(use => use.App.PrivateBytes < ListedFromBytes).ToList();
        return new MemorySection(
            group,
            shown,
            others.Count,
            others.Sum(use => use.App.PrivateBytes),
            uses.Sum(use => use.App.PrivateBytes));
    }
}
