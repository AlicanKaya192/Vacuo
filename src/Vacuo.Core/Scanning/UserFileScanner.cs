namespace Vacuo.Core.Scanning;

/// <summary>
/// Suggests the user's own files for the "Yours" bucket, file by file, from the folders in
/// <see cref="UserFileRules"/>. Only the folder itself is read, never its subfolders, and a
/// folder that is a link is not followed.
/// </summary>
public sealed class UserFileScanner(KnownRoots roots, TimeProvider time) : IScanner
{
    public ScanResult Scan()
    {
        var findings = new List<ScanFinding>();
        int skippedEntries = 0;
        DateTime now = time.GetUtcNow().UtcDateTime;

        foreach (UserFileRule rule in UserFileRules.All)
        {
            var folder = new DirectoryInfo(Path.Combine(roots.PathOf(rule.Root), rule.RelativeFolder));
            if (!folder.Exists || FileSystemWalk.IsLink(folder))
            {
                continue;
            }

            if (!FileSystemWalk.TryList(folder, out IReadOnlyList<FileSystemInfo> entries))
            {
                skippedEntries++;
                continue;
            }

            findings.AddRange(entries
                .OfType<FileInfo>()
                .Where(file => !FileSystemWalk.IsLink(file)
                    && rule.Extensions.Contains(file.Extension)
                    && now - file.LastWriteTimeUtc >= rule.MinimumAge)
                .Select(file => new ScanFinding(file.FullName, rule, new DirectorySize(file.Length, SkippedEntries: 0), file.LastWriteTimeUtc)));
        }

        return new ScanResult(findings, skippedEntries);
    }
}
