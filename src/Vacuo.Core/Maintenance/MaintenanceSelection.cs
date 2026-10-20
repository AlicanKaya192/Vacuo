using Vacuo.Core.Scanning;

namespace Vacuo.Core.Maintenance;

/// <summary>What the three scans Otomatik Bakım uses found.</summary>
public sealed record MaintenanceScan(IReadOnlyList<ScanFinding> Temp, IReadOnlyList<ScanFinding> KnownLocations, IReadOnlyList<ScanFinding> ProjectArtifacts);

/// <summary>
/// What Otomatik Bakım moves, by the chosen categories (ADR-004): temp items untouched for
/// <see cref="TempAge"/>, the package managers' download caches, and Python bytecode and Jupyter checkpoints.
/// Model caches (Hugging Face, torch) are never cleaned automatically: they take gigabytes to download again.
/// </summary>
public static class MaintenanceSelection
{
    public static TimeSpan TempAge { get; } = TimeSpan.FromDays(7);

    public static IReadOnlySet<string> DownloadCacheRules { get; } = new HashSet<string>(
        ["pip-cache", "npm-cache", "uv-cache", "pnpm-store", "conda-pkgs-anaconda", "conda-pkgs-miniconda"], StringComparer.Ordinal);

    public static IReadOnlySet<string> ProjectLeftoverRules { get; } = new HashSet<string>(
        ["python-bytecode", "jupyter-checkpoints"], StringComparer.Ordinal);

    public static IReadOnlyList<ScanFinding> Select(MaintenanceSettings settings, MaintenanceScan scan, DateTimeOffset now)
    {
        DateTime oldEnough = now.UtcDateTime - TempAge;
        var chosen = new List<ScanFinding>();
        if (settings.TempFiles)
        {
            chosen.AddRange(scan.Temp.Where(finding => finding.LastWriteTimeUtc <= oldEnough));
        }

        if (settings.DownloadCaches)
        {
            chosen.AddRange(scan.KnownLocations.Where(finding => DownloadCacheRules.Contains(finding.Rule.Id)));
        }

        if (settings.ProjectLeftovers)
        {
            chosen.AddRange(scan.ProjectArtifacts.Where(finding => ProjectLeftoverRules.Contains(finding.Rule.Id)));
        }

        return chosen;
    }
}
