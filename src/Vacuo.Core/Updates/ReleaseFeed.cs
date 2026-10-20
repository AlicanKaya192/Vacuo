using System.Text.Json;

namespace Vacuo.Core.Updates;

/// <summary>
/// Reads GitHub's release list (<c>/releases?per_page=30</c>) and picks the newest published Vacuo release:
/// drafts, prereleases and tags without <see cref="ReleaseVersion.TagPrefix"/> are skipped. A newer release is
/// offered only whole: its setup program and its SHA-256 file must both be there, at Vacuo's own release
/// downloads under that release's tag, so a changed answer cannot make the updater fetch anything else.
/// </summary>
public static class ReleaseFeed
{
    public const string DownloadPrefix = "https://github.com/AlicanKaya192/vacuo/releases/download/";

    public static UpdateCheck Read(string json, ReleaseVersion current)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return UpdateCheck.Failed("Unreadable release list: " + exception.Message);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return UpdateCheck.Failed("The answer is not a release list.");
            }

            (JsonElement Release, ReleaseVersion Version)? newest = null;
            foreach (JsonElement release in document.RootElement.EnumerateArray())
            {
                if (IsPublished(release)
                    && ReleaseVersion.TryParseTag(Text(release, "tag_name"), out ReleaseVersion? version)
                    && (newest is null || version!.CompareTo(newest.Value.Version) > 0))
                {
                    newest = (release, version!);
                }
            }

            if (newest is not { } chosen)
            {
                return UpdateCheck.Failed("No published release.");
            }

            if (chosen.Version.CompareTo(current) <= 0)
            {
                return UpdateCheck.Current;
            }

            return Offer(chosen.Release, chosen.Version) is { } offer
                ? new UpdateCheck(UpdateStatus.Newer, offer)
                : UpdateCheck.Failed($"Release {chosen.Version} lacks its setup program or checksum.");
        }
    }

    private static UpdateOffer? Offer(JsonElement release, ReleaseVersion version)
    {
        string tag = Text(release, "tag_name");
        string installerName = $"Vacuo-{version}-setup.exe";
        ReleaseAsset? installer = Asset(release, tag, installerName);
        ReleaseAsset? checksum = Asset(release, tag, installerName + ".sha256");
        return installer is { Size: > 0 } && checksum is not null
            ? new UpdateOffer(version, installer, checksum, Text(release, "body"))
            : null;
    }

    private static ReleaseAsset? Asset(JsonElement release, string tag, string name)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string url = DownloadPrefix + tag + "/" + name;
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (asset.ValueKind == JsonValueKind.Object && Text(asset, "name") == name && Text(asset, "browser_download_url") == url)
            {
                long size = asset.TryGetProperty("size", out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long bytes)
                    ? bytes
                    : 0;
                return new ReleaseAsset(name, url, size);
            }
        }

        return null;
    }

    private static bool IsPublished(JsonElement release) =>
        release.ValueKind == JsonValueKind.Object && !IsTrue(release, "draft") && !IsTrue(release, "prerelease");

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}
