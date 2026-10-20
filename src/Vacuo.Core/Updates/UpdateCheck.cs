namespace Vacuo.Core.Updates;

public enum UpdateStatus
{
    /// <summary>A newer release is published; <see cref="UpdateCheck.Offer"/> says what to download.</summary>
    Newer,

    /// <summary>The running version is the newest.</summary>
    Current,

    /// <summary>GitHub could not be reached: no internet, no name lookup, timed out.</summary>
    Offline,

    /// <summary>GitHub answered, but not with a usable release: 404 while the repository is private, rate limit, damaged list.</summary>
    Failed,
}

/// <summary>One downloadable file of a release.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>
/// A newer release: its full setup and that setup's SHA-256 file, the release notes, and, when the release has
/// one from the running version, a patch setup with its own SHA-256 file (K4).
/// </summary>
public sealed record UpdateOffer(
    ReleaseVersion Version,
    ReleaseAsset Installer,
    ReleaseAsset Checksum,
    string Notes,
    ReleaseAsset? Patch = null,
    ReleaseAsset? PatchChecksum = null)
{
    /// <summary>The patch first, as it is far smaller; the full setup when there is none or it failed once.</summary>
    public SetupChoice ChooseSetup(bool patchFailedBefore) =>
        Patch is { } patch && PatchChecksum is { } checksum && !patchFailedBefore
            ? new SetupChoice(patch, checksum, IsPatch: true)
            : new SetupChoice(Installer, Checksum, IsPatch: false);
}

/// <summary>The setup to download and the SHA-256 file to check it against.</summary>
public sealed record SetupChoice(ReleaseAsset Setup, ReleaseAsset Checksum, bool IsPatch);

/// <summary>What an update check found. <see cref="Detail"/> is for the log only, never shown as an error.</summary>
public sealed record UpdateCheck(UpdateStatus Status, UpdateOffer? Offer = null, string Detail = "")
{
    public static UpdateCheck Current { get; } = new(UpdateStatus.Current);

    public static UpdateCheck Offline(string detail) => new(UpdateStatus.Offline, Detail: detail);

    public static UpdateCheck Failed(string detail) => new(UpdateStatus.Failed, Detail: detail);
}
