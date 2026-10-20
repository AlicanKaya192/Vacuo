namespace Vacuo.Core.Uninstall;

/// <summary>One key under an <c>Uninstall</c> registry key, as read; any value may be missing.</summary>
public sealed record UninstallEntry(
    string? DisplayName,
    string? Publisher,
    string? DisplayVersion,
    int? EstimatedSizeKb,
    string? InstallDate,
    string? InstallLocation,
    string? UninstallString,
    bool SystemComponent,
    string? ParentKeyName,
    string? ReleaseType);

public sealed record InstalledProgram(
    string Name,
    string? Publisher,
    string? Version,
    long? SizeBytes,
    DateOnly? InstalledOn,
    string? InstallLocation,
    string UninstallCommand);

/// <summary>
/// The programs the Uninstall screen lists: what "Installed apps" in Windows Settings would show,
/// without system components, updates and entries that cannot be uninstalled. Only reads.
/// </summary>
public static class InstalledPrograms
{
    private static readonly HashSet<string> UpdateReleaseTypes =
        new(["Update", "Hotfix", "Security Update"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<InstalledProgram> From(IReadOnlyList<UninstallEntry> entries) => entries
        .Where(IsListed)
        .Select(entry => new InstalledProgram(
            entry.DisplayName!.Trim(),
            entry.Publisher?.Trim(),
            entry.DisplayVersion?.Trim(),
            entry.EstimatedSizeKb > 0 ? entry.EstimatedSizeKb.Value * 1024L : null,
            InstallDateOf(entry.InstallDate),
            entry.InstallLocation,
            entry.UninstallString!.Trim()))
        // 32-bit programs often appear under both registry views.
        .DistinctBy(program => (program.Name.ToUpperInvariant(), program.Version))
        .OrderBy(program => program.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static bool IsListed(UninstallEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.DisplayName)
        && !string.IsNullOrWhiteSpace(entry.UninstallString)
        && !entry.SystemComponent
        && string.IsNullOrEmpty(entry.ParentKeyName)
        && !(entry.ReleaseType is { } releaseType && UpdateReleaseTypes.Contains(releaseType));

    // Installers write yyyyMMdd; anything else is not trusted.
    private static DateOnly? InstallDateOf(string? value) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out DateOnly date) ? date : null;
}
