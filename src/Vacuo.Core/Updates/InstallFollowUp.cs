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

    /// <summary>A setup this updater downloaded, finished or not; the name <see cref="ReleaseFeed"/> accepts.</summary>
    public static bool IsOwnDownload(string fileName) => OwnDownload().IsMatch(fileName);

    [GeneratedRegex(@"^Vacuo-[0-9]+(\.[0-9]+){0,3}-setup\.exe(\.partial)?$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnDownload();
}
