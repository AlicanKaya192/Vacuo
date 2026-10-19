namespace Vacuo.Core.Scanning;

/// <summary>Measures the caches that live at fixed places under the user's folders.</summary>
public sealed class KnownLocationScanner(KnownRoots roots) : IScanner
{
    public ScanResult Scan()
    {
        List<ScanFinding> findings = KnownCacheRules.KnownLocations
            .Select(rule => (Rule: rule, Directory: new DirectoryInfo(Path.Combine(roots.PathOf(rule.Root), rule.RelativePath))))
            .Where(location => location.Directory.Exists && !FileSystemWalk.IsLink(location.Directory))
            .Select(location => ScanFinding.Measure(location.Directory, location.Rule))
            .ToList();

        return new ScanResult(findings, SkippedEntries: 0);
    }
}
