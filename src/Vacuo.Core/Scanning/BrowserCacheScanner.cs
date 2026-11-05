namespace Vacuo.Core.Scanning;

/// <summary>
/// Measures the cache folders of every profile of every known browser. Only reads; whether a
/// browser is open is checked separately, since it changes after the scan.
/// </summary>
public sealed class BrowserCacheScanner(KnownRoots roots) : IScanner
{
    private const string ChromiumProfileMarker = "Preferences";

    public ScanResult Scan()
    {
        var findings = new List<ScanFinding>();
        int skipped = 0;
        foreach (BrowserDefinition browser in BrowserCacheRules.Browsers)
        {
            var dataFolder = new DirectoryInfo(Path.Combine(roots.PathOf(browser.Root), browser.DataFolder));
            if (!dataFolder.Exists || FileSystemWalk.IsLink(dataFolder))
            {
                continue;
            }

            if (!TryFindProfiles(browser, dataFolder, out IReadOnlyList<DirectoryInfo> profiles))
            {
                skipped++;
                continue;
            }

            IReadOnlyList<BrowserCacheRule> rules = BrowserCacheRules.RulesFor(browser);
            findings.AddRange(profiles.SelectMany(profile => rules
                .Select(rule => (Rule: rule, Directory: new DirectoryInfo(Path.Combine(profile.FullName, rule.RelativePath))))
                .Where(cache => cache.Directory.Exists && !PassesThroughLink(profile, cache.Directory))
                .Select(cache => ScanFinding.Measure(cache.Directory, cache.Rule))));
        }

        return new ScanResult(findings, skipped);
    }

    private static bool TryFindProfiles(BrowserDefinition browser, DirectoryInfo dataFolder, out IReadOnlyList<DirectoryInfo> profiles)
    {
        if (browser.Layout == BrowserProfileLayout.ChromiumSingleProfile)
        {
            profiles = [dataFolder];
            return true;
        }

        if (!FileSystemWalk.TryList(dataFolder, out IReadOnlyList<FileSystemInfo> entries))
        {
            profiles = [];
            return false;
        }

        profiles = entries
            .OfType<DirectoryInfo>()
            .Where(directory => !FileSystemWalk.IsLink(directory))
            .Where(directory => browser.Layout == BrowserProfileLayout.FirefoxProfiles || IsChromiumProfile(directory))
            .ToList();
        return true;
    }

    private static bool IsChromiumProfile(DirectoryInfo directory) =>
        (directory.Name == "Default" || directory.Name.StartsWith("Profile ", StringComparison.Ordinal))
        && File.Exists(Path.Combine(directory.FullName, ChromiumProfileMarker));

    // A link anywhere between the profile and the cache (such as a moved "Cache" folder) would
    // lead outside the profile.
    private static bool PassesThroughLink(DirectoryInfo profile, DirectoryInfo cache)
    {
        for (DirectoryInfo? current = cache; current is not null && current.FullName.Length > profile.FullName.Length; current = current.Parent)
        {
            if (FileSystemWalk.IsLink(current))
            {
                return true;
            }
        }

        return false;
    }
}
