namespace Vacuo.Core.Ram;

/// <summary>One reading of the machine's memory; counters come from outside so the rule stays testable.</summary>
public sealed record MemorySnapshot(
    long TotalPhysicalBytes, long AvailablePhysicalBytes, long CommittedBytes, long CommitLimitBytes, double PageReadsPerSecond);

/// <summary>What the Memory card says instead of "% used".</summary>
public enum MemoryPressure
{
    Comfortable,
    Filling,
    Tight,
}

/// <summary>
/// Pressure from the commit charge, the RAM left and disk paging. A full-looking RAM alone is not
/// pressure: Windows keeps free memory busy as cache. The thresholds are a first choice; on this
/// machine (20 Oct 2026, 31.6 GB) a calm reading was commit 62 %, available 45 %, paging ~0/s.
/// </summary>
public static class MemoryPressureRule
{
    public const double FillingCommitRatio = 0.75;
    public const double TightCommitRatio = 0.90;
    public const double FillingAvailableRatio = 0.15;
    public const double TightAvailableRatio = 0.05;
    // Reads from disk per second that mean programs wait for memory, once RAM is short.
    public const double HeavyPageReadsPerSecond = 50;

    public static MemoryPressure Of(MemorySnapshot snapshot)
    {
        if (snapshot.CommitLimitBytes <= 0 || snapshot.TotalPhysicalBytes <= 0)
        {
            throw new ArgumentException("A snapshot needs a commit limit and physical memory.", nameof(snapshot));
        }

        double commit = (double)snapshot.CommittedBytes / snapshot.CommitLimitBytes;
        double available = (double)snapshot.AvailablePhysicalBytes / snapshot.TotalPhysicalBytes;
        bool ramShort = available < FillingAvailableRatio;

        if (commit >= TightCommitRatio
            || available < TightAvailableRatio
            || (ramShort && snapshot.PageReadsPerSecond >= HeavyPageReadsPerSecond))
        {
            return MemoryPressure.Tight;
        }

        return commit >= FillingCommitRatio || ramShort ? MemoryPressure.Filling : MemoryPressure.Comfortable;
    }
}
