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
}

public sealed record MoveFailure(string Path, MoveFailureReason Reason);

public sealed record MoveOutcome(IReadOnlyList<JournalEntry> Moved, IReadOnlyList<MoveFailure> Failed)
{
    public long FreedBytes => Moved.Sum(entry => entry.SizeBytes);
}

public sealed record DiskBucket(Bucket Bucket, IReadOnlyList<ScanFinding> Items, long TotalBytes);

/// <summary>
/// The Disk screen without its looks: buckets, the user's selection, the preview and the move.
/// Nothing starts selected, items in the "Keep" bucket can never be selected, and each selected
/// item goes to the Recycle Bin on its own, so one failure does not stop the others.
/// </summary>
public sealed class DiskScreen(ScanSummary summary)
{
    // Same code as UnauthorizedAccessException and the Shell's E_ACCESSDENIED.
    private const int AccessDenied = unchecked((int)0x8007_0005);

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

    public MoveOutcome MoveSelected(ActionRunner runner, IRecycleBin recycleBin)
    {
        var moved = new List<JournalEntry>();
        var failed = new List<MoveFailure>();

        foreach (ScanFinding finding in Preview())
        {
            if (!Path.Exists(finding.Path))
            {
                failed.Add(new MoveFailure(finding.Path, MoveFailureReason.Missing));
                continue;
            }

            try
            {
                moved.Add(runner.Run(new MoveToRecycleBinAction(finding.Path, recycleBin)));
                _findings.Remove(finding);
                _selected.Remove(finding.Path);
            }
            catch (ActionRefusedException refusal)
            {
                failed.Add(new MoveFailure(finding.Path, ReasonFor(refusal)));
            }
        }

        return new MoveOutcome(moved, failed);
    }

    private ScanFinding FindingAt(string path) =>
        _findings.FirstOrDefault(finding => string.Equals(finding.Path, path, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"'{path}' is not on the Disk screen.", nameof(path));

    private static MoveFailureReason ReasonFor(ActionRefusedException refusal) => refusal switch
    {
        FileInUseException => MoveFailureReason.InUse,
        NotRecyclableException => MoveFailureReason.NotRecyclable,
        { InnerException.HResult: AccessDenied } => MoveFailureReason.NoAccess,
        _ => MoveFailureReason.Refused,
    };
}
