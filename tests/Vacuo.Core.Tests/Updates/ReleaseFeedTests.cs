using System.Text.Json;
using Vacuo.Core.Updates;

namespace Vacuo.Core.Tests.Updates;

public class ReleaseFeedTests
{
    private const string Download = "https://github.com/AlicanKaya192/vacuo/releases/download/";
    private static readonly ReleaseVersion Current = Version("0.4.0");

    [Fact]
    public void NewerRelease_IsOfferedWithItsInstallerAndChecksum()
    {
        string json = Feed(Release("0.5.0", notes: "New things"));

        UpdateCheck check = ReleaseFeed.Read(json, Current);

        Assert.Equal(UpdateStatus.Newer, check.Status);
        UpdateOffer offer = check.Offer!;
        Assert.Equal("0.5.0", offer.Version.ToString());
        Assert.Equal("Vacuo-0.5.0-setup.exe", offer.Installer.Name);
        Assert.Equal(Download + "setup-0.5.0/Vacuo-0.5.0-setup.exe", offer.Installer.Url);
        Assert.Equal(68_000_000, offer.Installer.Size);
        Assert.Equal(Download + "setup-0.5.0/Vacuo-0.5.0-setup.exe.sha256", offer.Checksum.Url);
        Assert.Equal("New things", offer.Notes);
    }

    [Fact]
    public void HighestPublishedRelease_IsChosen_DraftsAndPrereleasesSkipped()
    {
        string json = Feed(
            Release("0.5.0"),
            Release("0.7.0", draft: true),
            Release("0.6.0", prerelease: true),
            Release("0.5.1"));

        UpdateCheck check = ReleaseFeed.Read(json, Current);

        Assert.Equal("0.5.1", check.Offer!.Version.ToString());
    }

    [Fact]
    public void TagsWithoutTheSetupPrefix_AreSkipped()
    {
        string json = Feed(Release("0.5.0", tag: "v0.9.0"), Release("0.5.0"));

        Assert.Equal("0.5.0", ReleaseFeed.Read(json, Current).Offer!.Version.ToString());
    }

    [Theory]
    [InlineData("0.4.0")]
    [InlineData("0.3.9")]
    public void NothingNewer_IsCurrent(string latest)
    {
        UpdateCheck check = ReleaseFeed.Read(Feed(Release(latest)), Current);

        Assert.Equal(UpdateStatus.Current, check.Status);
        Assert.Null(check.Offer);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{ not json")]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData("null")]
    public void NoReadableList_Fails(string json)
    {
        UpdateCheck check = ReleaseFeed.Read(json, Current);

        Assert.Equal(UpdateStatus.Failed, check.Status);
        Assert.Null(check.Offer);
    }

    [Fact]
    public void InstallerOutsideVacuosReleases_IsNotOffered()
    {
        string json = Feed(Release("0.5.0", installerUrl: "https://example.com/Vacuo-0.5.0-setup.exe"));

        UpdateCheck check = ReleaseFeed.Read(json, Current);

        Assert.Equal(UpdateStatus.Failed, check.Status);
        Assert.Null(check.Offer);
    }

    [Fact]
    public void InstallerFromAnotherReleasesFolder_IsNotOffered()
    {
        string json = Feed(Release("0.5.0", installerUrl: Download + "setup-0.1.0/Vacuo-0.5.0-setup.exe"));

        Assert.Equal(UpdateStatus.Failed, ReleaseFeed.Read(json, Current).Status);
    }

    [Fact]
    public void ReleaseWithoutChecksum_IsNotOffered()
    {
        string json = Feed(Release("0.5.0", withChecksum: false));

        Assert.Equal(UpdateStatus.Failed, ReleaseFeed.Read(json, Current).Status);
    }

    [Fact]
    public void EmptyInstaller_IsNotOffered()
    {
        string json = Feed(Release("0.5.0", installerSize: 0));

        Assert.Equal(UpdateStatus.Failed, ReleaseFeed.Read(json, Current).Status);
    }

    [Fact]
    public void ReleaseWithoutNotes_HasEmptyNotes()
    {
        string json = Feed(Release("0.5.0", notes: null));

        Assert.Equal("", ReleaseFeed.Read(json, Current).Offer!.Notes);
    }

    private static ReleaseVersion Version(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out ReleaseVersion? version));
        return version!;
    }

    private static string Feed(params object[] releases) => JsonSerializer.Serialize(releases);

    private static object Release(
        string version,
        string? tag = null,
        bool draft = false,
        bool prerelease = false,
        string? notes = "",
        string? installerUrl = null,
        long installerSize = 68_000_000,
        bool withChecksum = true)
    {
        tag ??= "setup-" + version;
        string installer = $"Vacuo-{version}-setup.exe";
        var assets = new List<object>
        {
            new { name = installer, size = installerSize, browser_download_url = installerUrl ?? $"{Download}{tag}/{installer}" },
        };
        if (withChecksum)
        {
            assets.Add(new { name = installer + ".sha256", size = 87, browser_download_url = $"{Download}{tag}/{installer}.sha256" });
        }

        return new { tag_name = tag, draft, prerelease, body = notes, assets };
    }
}
