using Vacuo.Core.Settings;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Settings;

public class SettingsStoreTests
{
    [Fact]
    public void NoFile_GivesTheDefaults()
    {
        using var tree = new TempTree();

        VacuoSettings settings = new SettingsStore(tree.PathOf(@"Vacuo\settings.json")).Load();

        Assert.Equal(VacuoSettings.Default, settings);
        Assert.Equal(LanguageChoice.Automatic, settings.Language);
        Assert.True(settings.CheckForUpdates);
        Assert.True(settings.LowDiskNotice);
    }

    [Fact]
    public void LowDiskNoticeTurnedOff_ComesBackOff()
    {
        using var tree = new TempTree();
        var store = new SettingsStore(tree.PathOf("settings.json"));

        store.Save(VacuoSettings.Default with { LowDiskNotice = false });

        Assert.False(store.Load().LowDiskNotice);
    }

    [Fact]
    public void Saved_ComesBackTheSame()
    {
        using var tree = new TempTree();
        var store = new SettingsStore(tree.PathOf(@"Vacuo\settings.json"));
        var chosen = new VacuoSettings(LanguageChoice.English, CheckForUpdates: false);

        store.Save(chosen);

        Assert.Equal(chosen, store.Load());
    }

    [Fact]
    public void DamagedFile_GivesTheDefaultsAndIsLeftAsItIs()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("settings.json");
        File.WriteAllText(path, "{ not json");

        VacuoSettings settings = new SettingsStore(path).Load();

        Assert.Equal(VacuoSettings.Default, settings);
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    [Fact]
    public void MissingFields_TakeTheirDefaults()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("settings.json");
        File.WriteAllText(path, """{ "Language": "Turkish" }""");

        VacuoSettings settings = new SettingsStore(path).Load();

        Assert.Equal(VacuoSettings.Default with { Language = LanguageChoice.Turkish }, settings);
    }

    // Starting with Windows lives in the Run key now; files written before still load.
    [Fact]
    public void FileWithTheOldStartupField_StillLoads()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("settings.json");
        File.WriteAllText(path, """{ "Language": "English", "StartWithWindows": false, "CheckForUpdates": false }""");

        VacuoSettings settings = new SettingsStore(path).Load();

        Assert.Equal(new VacuoSettings(LanguageChoice.English, CheckForUpdates: false), settings);
    }
}
