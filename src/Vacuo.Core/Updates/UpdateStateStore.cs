using System.Text.Json;

namespace Vacuo.Core.Updates;

/// <summary>
/// The updater's own notes in <c>%LOCALAPPDATA%\Vacuo\update.json</c>: when GitHub last answered a check, and
/// which version a started setup is installing. A missing or damaged file means neither, so the next panel
/// opening checks. Each save keeps the other note.
/// </summary>
public sealed class UpdateStateStore(string path)
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vacuo", "update.json");

    public DateTimeOffset? LoadLastCheck() => Load().LastCheck;

    public void SaveLastCheck(DateTimeOffset checkedAt) => Save(Load() with { LastCheck = checkedAt });

    public string? LoadPendingInstall() => Load().PendingInstall;

    public void SavePendingInstall(string version) => Save(Load() with { PendingInstall = version });

    public void ClearPendingInstall() => Save(Load() with { PendingInstall = null });

    private UpdateStateFile Load()
    {
        try
        {
            return (File.Exists(path) ? JsonSerializer.Deserialize<UpdateStateFile>(File.ReadAllText(path)) : null) ?? UpdateStateFile.Empty;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return UpdateStateFile.Empty;
        }
    }

    private void Save(UpdateStateFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(file));
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record UpdateStateFile(DateTimeOffset? LastCheck, string? PendingInstall)
    {
        public static UpdateStateFile Empty { get; } = new(null, null);
    }
}
