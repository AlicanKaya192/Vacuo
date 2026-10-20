using System.Text.RegularExpressions;

namespace Vacuo.Core.Updates;

public enum InstallFollowUp
{
    /// <summary>No setup was started since the last start.</summary>
    None,

    /// <summary>The setup went through: the running version is the one it installed.</summary>
    Updated,

    /// <summary>A setup was started but this is still another version: blocked, cancelled or failed.</summary>
    NotInstalled,
}

/// <summary>
/// The first start after the panel started a setup: "updated to X.Y.Z" when the running version is the one
/// being installed, nothing to say otherwise. Either way Vacuo's own setup files in the updates folder are
/// no longer needed (vacuo-kurulum-guncelleme-plani, "Silinenler"); nothing else there is Vacuo's to remove.
/// </summary>
public static partial class InstallFollowUps
{
    public static InstallFollowUp Of(string? pendingVersion, ReleaseVersion running)
    {
        if (pendingVersion is null)
        {
            return InstallFollowUp.None;
        }

        return ReleaseVersion.TryParse(pendingVersion, out ReleaseVersion? pending) && pending!.CompareTo(running) == 0
            ? InstallFollowUp.Updated
            : InstallFollowUp.NotInstalled;
    }

    /// <summary>
    /// A patch for <paramref name="offered"/> was started and this is still another version: the patch refuses
    /// a different installed version and leaves everything as it was, so the next try takes the full setup
    /// instead of the same patch again (as Odyssey's <c>patch_failed_before</c>).
    /// </summary>
    public static bool PatchFailedBefore(string? attemptedVersion, ReleaseVersion offered, ReleaseVersion running) =>
        ReleaseVersion.TryParse(attemptedVersion ?? string.Empty, out ReleaseVersion? attempted)
        && attempted!.CompareTo(offered) == 0
        && running.CompareTo(offered) != 0;

    /// <summary>A setup or patch this updater downloaded, finished or not; the names <see cref="ReleaseFeed"/> accepts.</summary>
    public static bool IsOwnDownload(string fileName) => OwnDownload().IsMatch(fileName);

    [GeneratedRegex(@"^Vacuo-[0-9]+(\.[0-9]+){0,3}-(setup|patch-[0-9]+(\.[0-9]+){0,3})\.exe(\.partial)?$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnDownload();
}
