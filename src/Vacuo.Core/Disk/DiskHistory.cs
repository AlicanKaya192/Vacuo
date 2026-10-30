namespace Vacuo.Core.Disk;

public sealed record FolderBytes(string Path, long Bytes);

/// <summary>One day's reading: how full the drive was and what each known folder held.</summary>
public sealed record DiskSnapshot(DateOnly Day, long UsedBytes, IReadOnlyList<FolderBytes> Folders)
{
    public static DiskSnapshot From(DateOnly day, long usedBytes, IReadOnlyList<CategorySize> sizes) =>
        new(day, usedBytes, sizes.Select(size => new FolderBytes(size.Root.Path, size.Size.Bytes)).ToList());
}

/// <summary>
/// The last <see cref="DaysKept"/> days of readings (Aşama 3g dilim 2), one per day: a later reading replaces
/// the day's earlier one. It grows only when something measures, so days may be missing.
/// </summary>
public sealed record DiskHistory(IReadOnlyList<DiskSnapshot> Snapshots)
{
    public const int DaysKept = 30;

    public static DiskHistory Empty { get; } = new([]);

    /// <summary>The readings in the window ending today, oldest first; for a day read twice, the later one.</summary>
    public IReadOnlyList<DiskSnapshot> ReadingsUpTo(DateOnly today)
    {
        DateOnly oldest = today.AddDays(-DaysKept);
        return Snapshots
            .Where(snapshot => snapshot.Day >= oldest && snapshot.Day <= today)
            .GroupBy(snapshot => snapshot.Day)
            .Select(day => day.Last())
            .OrderBy(snapshot => snapshot.Day)
            .ToList();
    }

    /// <summary>Days after the new one go too: the clock was set back, and they would never age out.</summary>
    public DiskHistory Recorded(DiskSnapshot snapshot)
    {
        DateOnly oldest = snapshot.Day.AddDays(-DaysKept);
        List<DiskSnapshot> kept = Snapshots
            .Where(earlier => earlier.Day >= oldest && earlier.Day < snapshot.Day)
            .OrderBy(earlier => earlier.Day)
            .ToList();
        kept.Add(snapshot);
        return new DiskHistory(kept);
    }
}
