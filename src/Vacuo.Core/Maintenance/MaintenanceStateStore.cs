using System.Text.Json;

namespace Vacuo.Core.Maintenance;

/// <summary>
/// When Otomatik Bakım last ran and what it did, in <c>%LOCALAPPDATA%\Vacuo\maintenance.json</c>. Written by
/// the helper, read by the tray (once a day) and Settings. A missing or damaged file means never.
/// </summary>
public sealed class MaintenanceStateStore(string path)
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vacuo", "maintenance.json");

    public DateTimeOffset? LoadLastRun() => Load()?.LastRun;

    public MaintenanceSummary? LoadLastSummary() => Load()?.Summary;

    public void SaveRun(DateTimeOffset at, MaintenanceSummary summary)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new StateFile(at, summary)));
        File.Move(temporary, path, overwrite: true);
    }

    private StateFile? Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<StateFile>(File.ReadAllText(path)) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record StateFile(DateTimeOffset? LastRun, MaintenanceSummary? Summary);
}
