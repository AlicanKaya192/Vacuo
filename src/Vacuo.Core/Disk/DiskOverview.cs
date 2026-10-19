using Vacuo.Core.Scanning;

namespace Vacuo.Core.Disk;

public sealed record DiskSegment(Bucket Bucket, long Bytes, double Fraction);

/// <summary>
/// What the Disk card's ring shows: each bucket's share of the whole drive and the free share.
/// Findings on other drives are left out, since the ring is about one drive.
/// </summary>
public sealed record DiskOverview(IReadOnlyList<DiskSegment> Segments, long FreeBytes, long TotalBytes, double FreeFraction)
{
    public static DiskOverview From(ScanSummary summary, string driveRoot, long freeBytes, long totalBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(freeBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(freeBytes, totalBytes);

        List<DiskSegment> segments = summary.Findings
            .Where(finding => string.Equals(Path.GetPathRoot(finding.Path), driveRoot, StringComparison.OrdinalIgnoreCase))
            .GroupBy(finding => finding.Rule.Bucket)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                long bytes = group.Sum(finding => finding.Size.Bytes);
                return new DiskSegment(group.Key, bytes, (double)bytes / totalBytes);
            })
            .ToList();

        return new DiskOverview(segments, freeBytes, totalBytes, (double)freeBytes / totalBytes);
    }
}
