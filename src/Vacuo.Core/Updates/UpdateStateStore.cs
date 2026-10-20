using System.Text.Json;

namespace Vacuo.Core.Updates;

/// <summary>
/// When GitHub last answered an update check, in <c>%LOCALAPPDATA%\Vacuo\update.json</c>. A missing or
/// damaged file means never, so the next panel opening checks.
/// </summary>
public sealed class UpdateStateStore(string path)
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vacuo", "update.json");

    public DateTimeOffset? LoadLastCheck()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<UpdateStateFile>(File.ReadAllText(path))?.LastCheck : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void SaveLastCheck(DateTimeOffset checkedAt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new UpdateStateFile(checkedAt)));
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record UpdateStateFile(DateTimeOffset? LastCheck);
}
