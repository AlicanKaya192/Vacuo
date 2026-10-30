namespace Vacuo.Core.Disk;

public sealed record FolderGrowth(string Path, long GrownBytes);

/// <summary>
/// What changed between the oldest and the latest reading of the last days: the drive's used space, and the
/// known folders that grew most. A folder not there on the oldest day grew from nothing.
/// </summary>
public sealed record DiskGrowth(int Days, long ChangeBytes, IReadOnlyList<FolderGrowth> Folders)
{
    private const int FoldersShown = 3;

    /// <summary>Null until two different days in the window can be compared.</summary>
    public static DiskGrowth? From(DiskHistory history, DateOnly today)
    {
        IReadOnlyList<DiskSnapshot> readings = history.ReadingsUpTo(today);
        if (readings.Count < 2)
        {
            return null;
        }

        DiskSnapshot first = readings[0];
        DiskSnapshot latest = readings[^1];
        return new DiskGrowth(
            latest.Day.DayNumber - first.Day.DayNumber,
            latest.UsedBytes - first.UsedBytes,
            GrewMost(first, latest));
    }

    private static List<FolderGrowth> GrewMost(DiskSnapshot first, DiskSnapshot latest)
    {
        Dictionary<string, long> before = first.Folders
            .DistinctBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(folder => folder.Path, folder => folder.Bytes, StringComparer.OrdinalIgnoreCase);
        return latest.Folders
            .DistinctBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase)
            .Select(folder => new FolderGrowth(folder.Path, folder.Bytes - before.GetValueOrDefault(folder.Path)))
            .Where(folder => folder.GrownBytes > 0)
            .OrderByDescending(folder => folder.GrownBytes)
            .ThenBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase)
            .Take(FoldersShown)
            .ToList();
    }
}
