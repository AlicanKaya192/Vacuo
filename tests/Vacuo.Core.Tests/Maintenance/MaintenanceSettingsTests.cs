using Vacuo.Core.Maintenance;
using Vacuo.Core.Settings;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Maintenance;

public class MaintenanceSettingsTests
{
    [Fact]
    public void Default_IsOff_WithTempFilesAndDownloadCachesChosenForWhenItIsTurnedOn()
    {
        MaintenanceSettings settings = MaintenanceSettings.Default;

        Assert.False(settings.Enabled);
        Assert.True(settings.TempFiles);
        Assert.True(settings.DownloadCaches);
        Assert.False(settings.ProjectLeftovers);
        Assert.False(settings.OrganizeDesktop);
        Assert.True(settings.Sound);
    }

    [Fact]
    public void OlderSettingsFile_GetsTheMaintenanceDefaults()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("settings.json");
        File.WriteAllText(path, """{ "Language": "Turkish", "CheckForUpdates": false }""");

        VacuoSettings settings = new SettingsStore(path).Load();

        Assert.Equal(LanguageChoice.Turkish, settings.Language);
        Assert.Equal(MaintenanceSettings.Default, settings.Maintenance);
    }

    [Fact]
    public void SavedMaintenanceChoice_ComesBackTheSame()
    {
        using var tree = new TempTree();
        var store = new SettingsStore(tree.PathOf("settings.json"));
        var chosen = VacuoSettings.Default with
        {
            Maintenance = new MaintenanceSettings(Enabled: true, TempFiles: false, DownloadCaches: true, ProjectLeftovers: true, OrganizeDesktop: false, Sound: false),
        };

        store.Save(chosen);

        Assert.Equal(chosen, store.Load());
    }

    [Fact]
    public void MissingMaintenanceFields_TakeTheirDefaults()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("settings.json");
        File.WriteAllText(path, """{ "Maintenance": { "Enabled": true } }""");

        MaintenanceSettings settings = new SettingsStore(path).Load().Maintenance;

        Assert.Equal(MaintenanceSettings.Default with { Enabled = true }, settings);
    }
}
