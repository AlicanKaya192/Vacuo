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

/// <summary>A newer release: its setup program, the setup's SHA-256 file and the release notes.</summary>
public sealed record UpdateOffer(ReleaseVersion Version, ReleaseAsset Installer, ReleaseAsset Checksum, string Notes);

/// <summary>What an update check found. <see cref="Detail"/> is for the log only, never shown as an error.</summary>
public sealed record UpdateCheck(UpdateStatus Status, UpdateOffer? Offer = null, string Detail = "")
{
    public static UpdateCheck Current { get; } = new(UpdateStatus.Current);

    public static UpdateCheck Offline(string detail) => new(UpdateStatus.Offline, Detail: detail);

    public static UpdateCheck Failed(string detail) => new(UpdateStatus.Failed, Detail: detail);
}
