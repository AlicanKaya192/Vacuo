namespace Vacuo.Core.Scanning;

/// <summary>
/// The table of user files Vacuo suggests for the "Yours" bucket. These are the user's own
/// files: they are only ever suggested, one file at a time, and never removed without a choice.
/// </summary>
public static class UserFileRules
{
    /// <summary>Installers usually stay in Downloads long after the program is installed.</summary>
    public static readonly TimeSpan InstallerMinimumAge = TimeSpan.FromDays(30);

    public static readonly IReadOnlyList<UserFileRule> All =
    [
        Suggest("screenshots", KnownRoot.Pictures, "Screenshots", [".png", ".jpg", ".jpeg"], TimeSpan.Zero),
        Suggest("old-installers", KnownRoot.UserProfile, "Downloads",
            [".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle"], InstallerMinimumAge),
    ];

    private static UserFileRule Suggest(string id, KnownRoot root, string relativeFolder, string[] extensions, TimeSpan minimumAge) =>
        new(id, Bucket.Yours, RebuildCost.CannotBeRebuilt, root, relativeFolder,
            new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase), minimumAge);
}
