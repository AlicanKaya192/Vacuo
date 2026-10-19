namespace Vacuo.Core.Scanning;

/// <summary>A folder a rule matched, with its measured size.</summary>
public sealed record ScanFinding(string Path, CacheRule Rule, DirectorySize Size, DateTime LastWriteTimeUtc)
{
    public static ScanFinding Measure(DirectoryInfo directory, CacheRule rule) =>
        new(directory.FullName, rule, DirectorySizer.Measure(directory), directory.LastWriteTimeUtc);
}

/// <summary>
/// What a scan found. <paramref name="SkippedEntries"/> counts folders the scan could not read
/// while searching, so the interface can say the result is incomplete.
/// </summary>
public sealed record ScanResult(IReadOnlyList<ScanFinding> Findings, int SkippedEntries);
