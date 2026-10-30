namespace Vacuo.Core.Disk;

/// <summary>A folder's change between two readings; negative when it shrank.</summary>
public sealed record FolderChange(string Path, long Bytes);

/// <summary>
/// What changed between two neighbouring readings. The change happened somewhere between them, so a step over
/// several days says only that. <see cref="OutsideBytes"/> is the drive's change the known folders do not explain.
/// </summary>
public sealed record DiskTimelineStep(
    DateOnly From,
    DateOnly To,
    long ChangeBytes,
    IReadOnlyList<FolderChange> Grew,
    FolderChange? Shrank,
    long OutsideBytes)
{
    public bool IsOneDay => To.DayNumber - From.DayNumber == 1;

    public bool IsNoticeable =>
        Math.Abs(ChangeBytes) >= DiskTimeline.NoticeableBytes
        || Grew.Count > 0
        || Shrank is not null
        || Math.Abs(OutsideBytes) >= DiskTimeline.NoticeableBytes;
}

/// <summary>
/// When the drive filled up (Aşama 3l), from the readings of the disk history; newest step first. It names folders,
/// not reasons: Vacuo knows which folder grew, never why. Only reads.
/// </summary>
public sealed record DiskTimeline(IReadOnlyList<DiskTimelineStep> Steps, DiskTimelineStep? Largest, long TotalBytes, int Days)
{
    /// <summary>Folders that move less than this come and go every day and would only crowd the list.</summary>
    public const long NoticeableBytes = 100L * 1024 * 1024;

    private const int GrewShown = 3;

    /// <summary>Null until two different days in the window can be compared.</summary>
    public static DiskTimeline? From(DiskHistory history, DateOnly today)
    {
        IReadOnlyList<DiskSnapshot> readings = history.ReadingsUpTo(today);
        if (readings.Count < 2)
        {
            return null;
        }

        List<DiskTimelineStep> steps = readings.Zip(readings.Skip(1), Step).Reverse().ToList();
        DiskTimelineStep? largest = steps.Where(step => step.ChangeBytes >= NoticeableBytes).MaxBy(step => step.ChangeBytes);
        return new DiskTimeline(
            steps,
            largest,
            readings[^1].UsedBytes - readings[0].UsedBytes,
            readings[^1].Day.DayNumber - readings[0].Day.DayNumber);
    }

    private static DiskTimelineStep Step(DiskSnapshot before, DiskSnapshot after)
    {
        List<FolderChange> changes = Changes(before, after);
        long change = after.UsedBytes - before.UsedBytes;
        return new DiskTimelineStep(
            before.Day,
            after.Day,
            change,
            changes
                .Where(folder => folder.Bytes >= NoticeableBytes)
                .OrderByDescending(folder => folder.Bytes)
                .ThenBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase)
                .Take(GrewShown)
                .ToList(),
            changes.Where(folder => folder.Bytes <= -NoticeableBytes).MinBy(folder => folder.Bytes),
            change - changes.Sum(folder => folder.Bytes));
    }

    // Folders in either reading: one that is new grew from nothing, one that is gone shrank to nothing.
    private static List<FolderChange> Changes(DiskSnapshot before, DiskSnapshot after)
    {
        Dictionary<string, long> earlier = Sizes(before);
        Dictionary<string, long> later = Sizes(after);
        return earlier.Keys
            .Union(later.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(path => new FolderChange(path, later.GetValueOrDefault(path) - earlier.GetValueOrDefault(path)))
            .ToList();
    }

    private static Dictionary<string, long> Sizes(DiskSnapshot snapshot) => snapshot.Folders
        .DistinctBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(folder => folder.Path, folder => folder.Bytes, StringComparer.OrdinalIgnoreCase);
}
