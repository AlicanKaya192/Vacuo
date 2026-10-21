using Vacuo.Core.Actions;
using Vacuo.Core.Journal;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Disk;

/// <summary>Why one item was not moved; the interface turns it into a sentence in either language.</summary>
public enum MoveFailureReason
{
    InUse,
    NoAccess,
    NotRecyclable,
    Missing,
    Refused,
    NoLongerDuplicate,
}

public sealed record MoveFailure(string Path, MoveFailureReason Reason);

public sealed record MoveOutcome(IReadOnlyList<JournalEntry> Moved, IReadOnlyList<MoveFailure> Failed)
{
    public long FreedBytes => Moved.Sum(entry => entry.SizeBytes);
}

/// <summary>
/// How far a move has got, for the progress bar (Alican, 21 October 2026): items handled so far, moved or
/// not, and their bytes. Reported after every item.
/// </summary>
public sealed record MoveProgress(int Done, int Total, long DoneBytes, long TotalBytes)
{
    /// <summary>By bytes, so one large folder weighs more than many small files; by count when sizes are unknown.</summary>
    public double Fraction => TotalBytes > 0 ? (double)DoneBytes / TotalBytes
        : Total > 0 ? (double)Done / Total
        : 1.0;

    /// <summary>Walks a list of items, reporting after each; <paramref name="handle"/> moves one.</summary>
    public static void Track<T>(IReadOnlyList<T> items, Func<T, long> sizeOf, IProgress<MoveProgress>? progress, Action<T> handle)
    {
        long totalBytes = items.Sum(sizeOf);
        long doneBytes = 0;
        for (int index = 0; index < items.Count; index++)
        {
            handle(items[index]);
            doneBytes += sizeOf(items[index]);
            progress?.Report(new MoveProgress(index + 1, items.Count, doneBytes, totalBytes));
        }
    }
}

public sealed record DiskBucket(Bucket Bucket, IReadOnlyList<ScanFinding> Items, long TotalBytes);

/// <summary>
/// The Disk screen without its looks: buckets, the user's selection, the preview and the move.
/// Nothing starts selected, items in the "Keep" bucket can never be selected, and each selected
/// item goes to the Recycle Bin on its own, so one failure does not stop the others.
/// </summary>
public sealed class DiskScreen(ScanSummary summary)
{
    private readonly List<ScanFinding> _findings = [.. summary.Findings];
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DiskBucket> Buckets => _findings
        .GroupBy(finding => finding.Rule.Bucket)
        .OrderBy(group => group.Key)
        .Select(group => new DiskBucket(
            group.Key,
            group.OrderByDescending(finding => finding.Size.Bytes).ToList(),
            group.Sum(finding => finding.Size.Bytes)))
        .ToList();

    public int SelectedCount => _selected.Count;

    public long SelectedBytes => Preview().Sum(finding => finding.Size.Bytes);

    /// <summary>Selected items from the "Yours" bucket: only the user knows whether they are still needed.</summary>
    public int SelectedYoursCount => Preview().Count(finding => finding.Rule.Bucket == Bucket.Yours);

    public bool IsSelected(string path) => _selected.Contains(path);

    public bool IsSelectable(string path) => FindingAt(path).Rule.Bucket != Bucket.Keep;

    public void Select(string path)
    {
        if (!IsSelectable(path))
        {
            throw new InvalidOperationException($"'{path}' belongs to the Keep bucket; it is only shown.");
        }

        _selected.Add(path);
    }

    public void Deselect(string path) => _selected.Remove(path);

    /// <summary>What a move would send to the Recycle Bin, largest first.</summary>
    public IReadOnlyList<ScanFinding> Preview() => _findings
        .Where(finding => _selected.Contains(finding.Path))
        .OrderByDescending(finding => finding.Size.Bytes)
        .ToList();

    public MoveOutcome MoveSelected(ActionRunner runner, IRecycleBin recycleBin, IProgress<MoveProgress>? progress = null)
    {
        var moved = new List<JournalEntry>();
        var failed = new List<MoveFailure>();

        MoveProgress.Track(Preview(), finding => finding.Size.Bytes, progress, finding =>
        {
            if (!Path.Exists(finding.Path))
            {
                failed.Add(new MoveFailure(finding.Path, MoveFailureReason.Missing));
                return;
            }

            try
            {
                moved.Add(runner.Run(new MoveToRecycleBinAction(finding.Path, recycleBin)));
                _findings.Remove(finding);
                _selected.Remove(finding.Path);
            }
            catch (ActionRefusedException refusal)
            {
                failed.Add(new MoveFailure(finding.Path, MoveFailures.ReasonFor(refusal)));
            }
        });

        return new MoveOutcome(moved, failed);
    }

    private ScanFinding FindingAt(string path) =>
        _findings.FirstOrDefault(finding => string.Equals(finding.Path, path, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"'{path}' is not on the Disk screen.", nameof(path));
}
