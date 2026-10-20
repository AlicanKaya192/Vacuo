using Vacuo.Core.Tests.Scanning;
using Vacuo.Core.Updates;

namespace Vacuo.Core.Tests.Updates;

public class InstallFollowUpTests
{
    [Fact]
    public void NoInstallStarted_IsNothing()
    {
        Assert.Equal(InstallFollowUp.None, InstallFollowUps.Of(pendingVersion: null, Version("0.5.0")));
    }

    [Fact]
    public void RunningTheInstalledVersion_IsUpdated()
    {
        Assert.Equal(InstallFollowUp.Updated, InstallFollowUps.Of("0.5.0", Version("0.5.0")));
    }

    [Theory]
    [InlineData("0.5.0", "0.4.0")]
    [InlineData("not a version", "0.4.0")]
    public void StillAnotherVersion_IsNotInstalled(string pending, string running)
    {
        Assert.Equal(InstallFollowUp.NotInstalled, InstallFollowUps.Of(pending, Version(running)));
    }

    [Theory]
    [InlineData("Vacuo-0.5.0-setup.exe")]
    [InlineData("Vacuo-0.5.0-setup.exe.partial")]
    [InlineData("Vacuo-1.10.2.3-setup.exe")]
    public void VacuosOwnDownloads_AreRecognised(string name)
    {
        Assert.True(InstallFollowUps.IsOwnDownload(name));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("Vacuo.exe")]
    [InlineData("Vacuo-0.5.0-setup.exe.sha256")]
    [InlineData("Other-0.5.0-setup.exe")]
    [InlineData("Vacuo-0.5.0-beta-setup.exe")]
    [InlineData("vacuo-0.5.0-setup.exe.bak")]
    public void AnythingElse_IsNotVacuos(string name)
    {
        Assert.False(InstallFollowUps.IsOwnDownload(name));
    }

    [Fact]
    public void PendingInstall_IsKeptWithTheLastCheck()
    {
        using var tree = new TempTree();
        var store = new UpdateStateStore(tree.PathOf("update.json"));
        var checkedAt = new DateTimeOffset(2026, 10, 20, 22, 0, 0, TimeSpan.Zero);
        store.SaveLastCheck(checkedAt);

        store.SavePendingInstall("0.5.0");

        Assert.Equal("0.5.0", store.LoadPendingInstall());
        Assert.Equal(checkedAt, store.LoadLastCheck());
    }

    [Fact]
    public void ClearedPendingInstall_IsGone_AndTheLastCheckStays()
    {
        using var tree = new TempTree();
        var store = new UpdateStateStore(tree.PathOf("update.json"));
        var checkedAt = new DateTimeOffset(2026, 10, 20, 22, 0, 0, TimeSpan.Zero);
        store.SaveLastCheck(checkedAt);
        store.SavePendingInstall("0.5.0");

        store.ClearPendingInstall();

        Assert.Null(store.LoadPendingInstall());
        Assert.Equal(checkedAt, store.LoadLastCheck());
    }

    [Fact]
    public void SavingTheLastCheck_KeepsThePendingInstall()
    {
        using var tree = new TempTree();
        var store = new UpdateStateStore(tree.PathOf("update.json"));
        store.SavePendingInstall("0.5.0");

        store.SaveLastCheck(DateTimeOffset.UnixEpoch);

        Assert.Equal("0.5.0", store.LoadPendingInstall());
    }

    private static ReleaseVersion Version(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out ReleaseVersion? version));
        return version!;
    }
}
