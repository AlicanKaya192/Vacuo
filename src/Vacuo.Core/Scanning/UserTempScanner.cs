namespace Vacuo.Core.Scanning;

/// <summary>A leftover that programs or Windows leave behind, such as a temp file.</summary>
public sealed record LeftoverRule(string Id, Bucket Bucket, RebuildCost Cost) : CacheRule(Id, Bucket, Cost);

/// <summary>
/// Lists the user's temp folder item by item: every top-level file and folder, with its size.
/// Links are not followed or listed. Whether an item is in use is checked when it is moved.
/// </summary>
public sealed class UserTempScanner(string tempFolder) : IScanner
{
    public static readonly LeftoverRule Rule = new("user-temp", Bucket.RebuildsItself, RebuildCost.RegeneratedAutomatically);

    public ScanResult Scan()
    {
        var folder = new DirectoryInfo(tempFolder);
        if (!folder.Exists || FileSystemWalk.IsLink(folder))
        {
            return new ScanResult([], SkippedEntries: 0);
        }

        if (!FileSystemWalk.TryList(folder, out IReadOnlyList<FileSystemInfo> entries))
        {
            return new ScanResult([], SkippedEntries: 1);
        }

        List<ScanFinding> findings = entries
            .Where(entry => !FileSystemWalk.IsLink(entry))
            .Select(entry => entry switch
            {
                DirectoryInfo directory => ScanFinding.Measure(directory, Rule),
                FileInfo file => new ScanFinding(file.FullName, Rule, new DirectorySize(file.Length, SkippedEntries: 0), file.LastWriteTimeUtc),
                _ => throw new InvalidOperationException($"'{entry.FullName}' is neither a file nor a folder."),
            })
            .ToList();
        return new ScanResult(findings, SkippedEntries: 0);
    }
}
