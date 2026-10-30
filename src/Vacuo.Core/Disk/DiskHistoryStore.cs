using System.Text.Json;

namespace Vacuo.Core.Disk;

/// <summary>
/// The readings in <c>%LOCALAPPDATA%\Vacuo\disk-history.json</c>; they never leave this computer. Written by the
/// panel and the helper, read by the helper for the low-disk notice. A missing or damaged file means no history.
/// </summary>
public sealed class DiskHistoryStore(string path)
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vacuo", "disk-history.json");

    public DiskHistory Load()
    {
        try
        {
            DiskHistory? history = File.Exists(path) ? JsonSerializer.Deserialize<DiskHistory>(File.ReadAllText(path)) : null;
            return IsWhole(history) ? history! : DiskHistory.Empty;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return DiskHistory.Empty;
        }
    }

    /// <summary>A file that cannot be written only loses this reading; the history returned still has it.</summary>
    public DiskHistory Record(DiskSnapshot snapshot)
    {
        DiskHistory history = Load().Recorded(snapshot);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(history));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next reading tries again.
        }

        return history;
    }

    // JSON can be valid and still leave nulls where the types promise none.
    private static bool IsWhole(DiskHistory? history) =>
        history?.Snapshots is { } snapshots
        && snapshots.All(snapshot => snapshot?.Folders is { } folders && folders.All(folder => folder?.Path is not null));
}
