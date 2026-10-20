using System.Text.Json;
using System.Text.Json.Serialization;
using Vacuo.Core.Maintenance;

namespace Vacuo.Core.Settings;

public enum LanguageChoice
{
    Automatic,
    Turkish,
    English,
}

/// <summary>
/// What the user chose in Settings. Checking for updates is on unless turned off (Alican, 20 October
/// 2026; vacuo-kurallar 6). Starting with Windows is not here: the Run key itself says it
/// (<see cref="StartupRegistration"/>).
/// </summary>
public sealed record VacuoSettings(LanguageChoice Language, bool CheckForUpdates)
{
    public static VacuoSettings Default { get; } = new(LanguageChoice.Automatic, CheckForUpdates: true);

    public MaintenanceSettings Maintenance { get; init; } = MaintenanceSettings.Default;
}

/// <summary>
/// The settings file, <c>%LOCALAPPDATA%\Vacuo\settings.json</c>. A missing or damaged file means the
/// defaults; a damaged one is left as it is until the user saves. Saving replaces the file whole.
/// </summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vacuo", "settings.json");

    public VacuoSettings Load()
    {
        try
        {
            SettingsFile? file = File.Exists(path) ? JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(path), JsonOptions) : null;
            return file is null
                ? VacuoSettings.Default
                : new VacuoSettings(
                    file.Language ?? VacuoSettings.Default.Language,
                    file.CheckForUpdates ?? VacuoSettings.Default.CheckForUpdates)
                {
                    Maintenance = file.Maintenance?.ToSettings() ?? MaintenanceSettings.Default,
                };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return VacuoSettings.Default;
        }
    }

    public void Save(VacuoSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        var file = new SettingsFile(settings.Language, settings.CheckForUpdates, MaintenanceFile.From(settings.Maintenance));
        File.WriteAllText(temporary, JsonSerializer.Serialize(file, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    // Every field may be missing in an older or hand-edited file.
    private sealed record SettingsFile(LanguageChoice? Language, bool? CheckForUpdates, MaintenanceFile? Maintenance);

    private sealed record MaintenanceFile(
        bool? Enabled, bool? TempFiles, bool? DownloadCaches, bool? ProjectLeftovers, bool? OrganizeDesktop, bool? Sound)
    {
        public static MaintenanceFile From(MaintenanceSettings settings) => new(
            settings.Enabled, settings.TempFiles, settings.DownloadCaches, settings.ProjectLeftovers, settings.OrganizeDesktop, settings.Sound);

        public MaintenanceSettings ToSettings()
        {
            MaintenanceSettings defaults = MaintenanceSettings.Default;
            return new MaintenanceSettings(
                Enabled ?? defaults.Enabled,
                TempFiles ?? defaults.TempFiles,
                DownloadCaches ?? defaults.DownloadCaches,
                ProjectLeftovers ?? defaults.ProjectLeftovers,
                OrganizeDesktop ?? defaults.OrganizeDesktop,
                Sound ?? defaults.Sound);
        }
    }
}

/// <summary>The language the interface uses: on Automatic, Turkish on a Turkish Windows and English on any other.</summary>
public static class LanguageRule
{
    public const string Turkish = "tr-TR";
    public const string English = "en-US";

    public static string Resolve(LanguageChoice choice, string windowsLanguage) => choice switch
    {
        LanguageChoice.Turkish => Turkish,
        LanguageChoice.English => English,
        _ => IsTurkish(windowsLanguage) ? Turkish : English,
    };

    internal static bool IsTurkish(string language) =>
        language.Equals("tr", StringComparison.OrdinalIgnoreCase) || language.StartsWith("tr-", StringComparison.OrdinalIgnoreCase);
}
