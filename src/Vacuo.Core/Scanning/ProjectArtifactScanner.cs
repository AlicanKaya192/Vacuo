namespace Vacuo.Core.Scanning;

/// <summary>
/// Walks the project folders the user chose and reports folders a project-artifact rule matches,
/// such as <c>node_modules</c>. It never descends into a match, a link, a dot folder or a folder
/// of installed software (<see cref="KnownCacheRules.InstalledSoftwareFolders"/>).
/// </summary>
public sealed class ProjectArtifactScanner : IScanner
{
    private readonly IReadOnlyList<string> _roots;

    public ProjectArtifactScanner(IReadOnlyList<string> roots)
    {
        ScanRootGuard guard = ScanRootGuard.ForThisMachine();
        foreach (string root in roots)
        {
            guard.EnsureAllowed(root);
        }

        _roots = roots;
    }

    public ScanResult Scan()
    {
        var findings = new List<ScanFinding>();
        int skippedEntries = 0;
        var pending = new Stack<DirectoryInfo>(_roots.Select(root => new DirectoryInfo(root)));

        while (pending.TryPop(out DirectoryInfo? current))
        {
            if (!FileSystemWalk.TryList(current, out IReadOnlyList<FileSystemInfo> entries))
            {
                skippedEntries++;
                continue;
            }

            foreach (DirectoryInfo child in entries.OfType<DirectoryInfo>().Where(child => !FileSystemWalk.IsLink(child)))
            {
                ProjectArtifactRule? rule = KnownCacheRules.ProjectArtifacts.FirstOrDefault(rule => rule.Matches(child));
                if (rule is not null)
                {
                    findings.Add(ScanFinding.Measure(child, rule));
                }
                else if (MayHoldProjects(child))
                {
                    pending.Push(child);
                }
            }
        }

        return new ScanResult(findings, skippedEntries);
    }

    // Dot folders hold tool data (.git, .vscode, .cache) rather than projects.
    private static bool MayHoldProjects(DirectoryInfo directory) =>
        !directory.Name.StartsWith('.') && !KnownCacheRules.InstalledSoftwareFolders.Contains(directory.Name);
}
