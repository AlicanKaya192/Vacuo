using System.Text.Json;
using Vacuo.Core.Tests.Scanning;
using Vacuo.Core.Updates;

namespace Vacuo.Core.Tests.Updates;

public class PatchChoiceTests
{
    private const string Download = "https://github.com/AlicanKaya192/vacuo/releases/download/setup-0.5.0/";

    [Fact]
    public void PatchFromTheRunningVersion_IsOffered()
    {
        UpdateOffer offer = Read("0.4.0", "Vacuo-0.5.0-patch-0.4.0.exe")!;

        Assert.Equal("Vacuo-0.5.0-patch-0.4.0.exe", offer.Patch!.Name);
        Assert.Equal(Download + "Vacuo-0.5.0-patch-0.4.0.exe.sha256", offer.PatchChecksum!.Url);
    }

    [Fact]
    public void PatchFromAnotherVersion_IsNotOffered()
    {
        UpdateOffer offer = Read("0.3.0", "Vacuo-0.5.0-patch-0.4.0.exe")!;

        Assert.Null(offer.Patch);
        Assert.Null(offer.PatchChecksum);
    }

    [Fact]
    public void PatchWithoutItsChecksum_IsNotOffered()
    {
        UpdateOffer offer = Read("0.4.0", "Vacuo-0.5.0-patch-0.4.0.exe", patchChecksum: false)!;

        Assert.Null(offer.Patch);
    }

    [Fact]
    public void Patch_IsChosenFirst()
    {
        UpdateOffer offer = Read("0.4.0", "Vacuo-0.5.0-patch-0.4.0.exe")!;

        SetupChoice choice = offer.ChooseSetup(patchFailedBefore: false);

        Assert.True(choice.IsPatch);
        Assert.Equal(offer.Patch, choice.Setup);
        Assert.Equal(offer.PatchChecksum, choice.Checksum);
    }

    [Fact]
    public void AfterAFailedPatch_TheFullSetupIsChosen()
    {
        UpdateOffer offer = Read("0.4.0", "Vacuo-0.5.0-patch-0.4.0.exe")!;

        SetupChoice choice = offer.ChooseSetup(patchFailedBefore: true);

        Assert.False(choice.IsPatch);
        Assert.Equal(offer.Installer, choice.Setup);
        Assert.Equal(offer.Checksum, choice.Checksum);
    }

    [Fact]
    public void WithoutAPatch_TheFullSetupIsChosen()
    {
        UpdateOffer offer = Read("0.3.0", "Vacuo-0.5.0-patch-0.4.0.exe")!;

        Assert.False(offer.ChooseSetup(patchFailedBefore: false).IsPatch);
    }

    [Theory]
    [InlineData("0.5.0", "0.4.0", true)]
    [InlineData("0.5.0", "0.5.0", false)]
    [InlineData("0.6.0", "0.4.0", false)]
    [InlineData(null, "0.4.0", false)]
    public void PatchFailedBefore_WhenTriedForThisVersionAndStillOnTheOld(string? attempted, string running, bool failed)
    {
        Assert.Equal(failed, InstallFollowUps.PatchFailedBefore(attempted, Version("0.5.0"), Version(running)));
    }

    [Fact]
    public void PatchAttempt_IsKeptWithTheOtherNotes()
    {
        using var tree = new TempTree();
        var store = new UpdateStateStore(tree.PathOf("update.json"));
        store.SavePendingInstall("0.5.0");

        store.SavePatchAttempt("0.5.0");

        Assert.Equal("0.5.0", store.LoadPatchAttempt());
        Assert.Equal("0.5.0", store.LoadPendingInstall());
    }

    [Theory]
    [InlineData("Vacuo-0.5.0-patch-0.4.0.exe")]
    [InlineData("Vacuo-0.5.0-patch-0.4.0.exe.partial")]
    public void PatchDownloads_AreVacuosOwn(string name)
    {
        Assert.True(InstallFollowUps.IsOwnDownload(name));
    }

    private static UpdateOffer? Read(string running, string patchName, bool patchChecksum = true)
    {
        var assets = new List<object>
        {
            Asset("Vacuo-0.5.0-setup.exe", 68_000_000),
            Asset("Vacuo-0.5.0-setup.exe.sha256", 87),
            Asset(patchName, 3_000_000),
        };
        if (patchChecksum)
        {
            assets.Add(Asset(patchName + ".sha256", 93));
        }

        string json = JsonSerializer.Serialize(new[] { new { tag_name = "setup-0.5.0", draft = false, prerelease = false, body = "", assets } });
        return ReleaseFeed.Read(json, Version(running)).Offer;
    }

    private static object Asset(string name, long size) => new { name, size, browser_download_url = Download + name };

    private static ReleaseVersion Version(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out ReleaseVersion? version));
        return version!;
    }
}
