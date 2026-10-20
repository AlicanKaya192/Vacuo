using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;

namespace Vacuo.Core.Duplicates;

public sealed record PickedCopy(string Path, long Bytes);

/// <summary>
/// The duplicate files screen without its looks: the groups, the copies the user picked, the preview and
/// the move. Nothing starts picked and one copy of each group always stays. The picked copies go to the
/// Recycle Bin as one journal batch, so the Journal undoes them together; each moves on its own, so one
/// failure does not stop the others.
/// </summary>
public sealed class DuplicatesScreen
{
    private List<DuplicateGroup> _groups;
    private DuplicateSelection _selection;

    public DuplicatesScreen(DuplicateScan scan)
    {
        _groups = [.. scan.Groups];
        _selection = new DuplicateSelection(_groups);
        SkippedEntries = scan.SkippedEntries;
    }

    public IReadOnlyList<DuplicateGroup> Groups => _groups;

    public int SkippedEntries { get; }

    public long WastedBytes => _groups.Sum(group => group.WastedBytes);

    public int PickedCount => _selection.Picked.Count;

    public long PickedBytes => _selection.PickedBytes;

    public bool IsPicked(string path) => _selection.IsPicked(path);

    public bool CanPick(string path) => _selection.CanPick(path);

    public bool TryPick(string path) => _selection.TryPick(path);

    public void Unpick(string path) => _selection.Unpick(path);

    /// <summary>What a move would send to the Recycle Bin, largest first.</summary>
    public IReadOnlyList<PickedCopy> Preview() => _groups
        .SelectMany(group => group.Files.Where(file => IsPicked(file.Path)).Select(file => new PickedCopy(file.Path, group.FileBytes)))
        .OrderByDescending(copy => copy.Bytes)
        .ToList();

    public MoveOutcome MoveSelected(ActionRunner runner, IRecycleBin recycleBin)
    {
        var batchId = Guid.NewGuid();
        var moved = new List<JournalEntry>();
        var failed = new List<MoveFailure>();
        var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DuplicateGroup group in _groups)
        {
            foreach (DuplicateFile copy in group.Files.Where(file => IsPicked(file.Path)))
            {
                if (WhyNotMovable(copy, group) is { } reason)
                {
                    failed.Add(new MoveFailure(copy.Path, reason));
                    continue;
                }

                try
                {
                    moved.Add(runner.Run(new MoveToRecycleBinAction(copy.Path, recycleBin), batchId, JournalBatchKind.Duplicates));
                    gone.Add(copy.Path);
                }
                catch (ActionRefusedException refusal)
                {
                    failed.Add(new MoveFailure(copy.Path, MoveFailures.ReasonFor(refusal)));
                }
            }
        }

        Forget(gone);
        return new MoveOutcome(moved, failed);
    }

    // The scan is a snapshot: a copy edited since, or a group whose kept copies are gone or changed,
    // is no longer a duplicate, and moving it could take away the last copy of a file.
    private MoveFailureReason? WhyNotMovable(DuplicateFile copy, DuplicateGroup group)
    {
        if (!File.Exists(copy.Path))
        {
            return MoveFailureReason.Missing;
        }

        bool keptCopyRemains = group.Files.Any(file => !IsPicked(file.Path) && HasLength(file.Path, group.FileBytes));
        return HasLength(copy.Path, group.FileBytes) && keptCopyRemains ? null : MoveFailureReason.NoLongerDuplicate;
    }

    private static bool HasLength(string path, long bytes)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length == bytes;
    }

    // Copies that failed stay picked, so the user can try them again.
    private void Forget(HashSet<string> gone)
    {
        List<string> stillPicked = _selection.Picked.Where(path => !gone.Contains(path)).ToList();
        _groups = _groups
            .Select(group => group with { Files = group.Files.Where(file => !gone.Contains(file.Path)).ToList() })
            .Where(group => group.Files.Count > 1)
            .ToList();
        _selection = new DuplicateSelection(_groups);
        foreach (string path in stillPicked)
        {
            _selection.TryPick(path);
        }
    }
}
