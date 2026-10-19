namespace Vacuo.Core.Scanning;

public sealed record BucketTotal(Bucket Bucket, long Bytes, int Count);

/// <summary>
/// Several scans put together: findings largest first, totals per bucket, and every folder
/// that could not be read, whether while searching or while measuring a finding.
/// </summary>
public sealed record ScanSummary(IReadOnlyList<ScanFinding> Findings, IReadOnlyList<BucketTotal> BucketTotals, long TotalBytes, int SkippedEntries)
{
    public static ScanSummary Combine(IEnumerable<ScanResult> results)
    {
        IReadOnlyList<ScanResult> all = results.ToList();
        List<ScanFinding> findings = all.SelectMany(result => result.Findings)
            .OrderByDescending(finding => finding.Size.Bytes)
            .ToList();

        return FromSorted(findings, all.Sum(result => result.SkippedEntries));
    }

    /// <summary>The same summary after the given paths are gone, e.g. moved to the Recycle Bin.</summary>
    public ScanSummary Without(IEnumerable<string> paths)
    {
        var gone = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        List<ScanFinding> remaining = Findings.Where(finding => !gone.Contains(finding.Path)).ToList();
        int skippedOutsideFindings = SkippedEntries - Findings.Sum(finding => finding.Size.SkippedEntries);
        return FromSorted(remaining, skippedOutsideFindings);
    }

    private static ScanSummary FromSorted(List<ScanFinding> findings, int skippedOutsideFindings)
    {
        List<BucketTotal> bucketTotals = findings
            .GroupBy(finding => finding.Rule.Bucket)
            .OrderBy(group => group.Key)
            .Select(group => new BucketTotal(group.Key, group.Sum(finding => finding.Size.Bytes), group.Count()))
            .ToList();

        int skippedEntries = skippedOutsideFindings + findings.Sum(finding => finding.Size.SkippedEntries);

        return new ScanSummary(findings, bucketTotals, findings.Sum(finding => finding.Size.Bytes), skippedEntries);
    }
}
