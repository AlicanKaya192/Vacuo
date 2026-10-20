namespace Vacuo.Core.Maintenance;

/// <summary>
/// Otomatik Bakım's choices in Settings (ADR-004). Off until the user turns it on (vacuo-kurallar 2, #37);
/// once on, temp files and download caches are cleaned (Alican, 20 October 2026); Python and Jupyter
/// leftovers and tidying the Desktop each have their own switch, off. The sound can be turned off.
/// </summary>
public sealed record MaintenanceSettings(bool Enabled, bool TempFiles, bool DownloadCaches, bool ProjectLeftovers, bool OrganizeDesktop, bool Sound)
{
    public static MaintenanceSettings Default { get; } = new(
        Enabled: false, TempFiles: true, DownloadCaches: true, ProjectLeftovers: false, OrganizeDesktop: false, Sound: true);

    /// <summary>At least one kind of work is chosen.</summary>
    public bool HasWork => TempFiles || DownloadCaches || ProjectLeftovers || OrganizeDesktop;
}
