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
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.CheckForUpdates);
    }

    [Fact]
    public void Saved_ComesBackTheSame()
    {
        using var tree = new TempTree();
        var store = new SettingsStore(tree.PathOf(@"Vacuo\settings.json"));
        var chosen = new VacuoSettings(LanguageChoice.English, StartWithWindows: false, CheckForUpdates: false);

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
}
