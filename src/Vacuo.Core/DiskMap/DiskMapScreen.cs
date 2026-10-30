using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;

namespace Vacuo.Core.DiskMap;

/// <summary>
/// The disk map without its looks: where the user is, what is selected, the preview and the move. Nothing starts
/// selected. The selection goes to the Recycle Bin as one journal batch, so the Journal undoes it together; each
/// item moves on its own, so one failure does not stop the others. What moved stays in the used space until the bin
/// is emptied, so the unread box keeps its size until the next scan.
/// </summary>
public sealed class DiskMapScreen
{
    private readonly MapProtection _protection;
    private readonly long _unreadBytes;
    private readonly List<FolderNode> _trail;
    private readonly List<FolderNode> _selected = [];

    public DiskMapScreen(FolderTreeScan scan, MapProtection protection)
    {
        _protection = protection;
        _unreadBytes = scan.UnreadBytes;
        Root = scan.Root;
        _trail = [scan.Root];
    }

    public FolderNode Root { get; }

    public FolderNode Current => _trail[^1];

    /// <summary>From the root down to <see cref="Current"/>.</summary>
    public IReadOnlyList<FolderNode> Trail => _trail;

    public int SelectedCount => _selected.Count;

    public long SelectedBytes => _selected.Sum(node => node.Bytes);

    public bool CanEnter(FolderNode node) => node.Children.Count > 0;

    /// <summary>Goes into a node below <see cref="Current"/>, which may be an inner box two levels down.</summary>
    public void Enter(FolderNode node)
    {
        var steps = new List<FolderNode>();
        for (FolderNode? step = node; !ReferenceEquals(step, Current); step = step.Parent)
        {
            if (step is null)
            {
                throw new ArgumentException($"'{node.Name}' is not below the folder being viewed.", nameof(node));
            }

            steps.Add(step);
        }

        steps.Reverse();
        _trail.AddRange(steps);
    }

    public void Up()
    {
        if (_trail.Count > 1)
        {
            _trail.RemoveAt(_trail.Count - 1);
        }
    }

    public void GoTo(FolderNode node)
    {
        int at = _trail.IndexOf(node);
        if (at < 0)
        {
            throw new ArgumentException($"'{node.Name}' is not on the trail.", nameof(node));
        }

        _trail.RemoveRange(at + 1, _trail.Count - at - 1);
    }

    public MapLock LockOf(FolderNode node) => _protection.LockOf(node.FullPath);

    /// <summary>True also for what is inside a selected folder.</summary>
    public bool IsSelected(FolderNode node)
    {
        for (FolderNode? step = node; step is not null; step = step.Parent)
        {
            if (_selected.Contains(step))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Selecting a folder replaces what was selected inside it, so nothing counts twice.</summary>
    public bool TrySelect(FolderNode node)
    {
        if (LockOf(node) != MapLock.None || IsSelected(node))
        {
            return false;
        }

        _selected.RemoveAll(selected => IsBelow(selected, node));
        _selected.Add(node);
        return true;
    }

    public void Unselect(FolderNode node) => _selected.Remove(node);

    /// <summary>What a move would send to the Recycle Bin, largest first.</summary>
    public IReadOnlyList<FolderNode> Preview() => _selected.OrderByDescending(node => node.Bytes).ToList();

    public IReadOnlyList<MapBox> Boxes(double width, double height) =>
        DiskMapLayout.Arrange(Current, ReferenceEquals(Current, Root) ? _unreadBytes : 0, width, height);

    public MoveOutcome MoveSelected(ActionRunner runner, IRecycleBin recycleBin, IProgress<MoveProgress>? progress = null)
    {
        var batchId = Guid.NewGuid();
        var moved = new List<JournalEntry>();
        var failed = new List<MoveFailure>();

        MoveProgress.Track(Preview(), node => node.Bytes, progress, node =>
        {
            string path = node.FullPath;
            if (LockOf(node) != MapLock.None)
            {
                failed.Add(new MoveFailure(path, MoveFailureReason.Refused));
                return;
            }

            if (!Path.Exists(path))
            {
                failed.Add(new MoveFailure(path, MoveFailureReason.Missing));
                return;
            }

            try
            {
                moved.Add(runner.Run(new MoveToRecycleBinAction(path, recycleBin), batchId, JournalBatchKind.DiskMap));
                _selected.Remove(node);
                node.Detach();
            }
            catch (ActionRefusedException refusal)
            {
                failed.Add(new MoveFailure(path, MoveFailures.ReasonFor(refusal)));
            }
        });

        LeaveWhatIsGone();
        return new MoveOutcome(moved, failed);
    }

    private void LeaveWhatIsGone()
    {
        int gone = _trail.FindIndex(node => node.IsDetached);
        if (gone >= 0)
        {
            _trail.RemoveRange(gone, _trail.Count - gone);
        }
    }

    private static bool IsBelow(FolderNode node, FolderNode folder)
    {
        for (FolderNode? step = node.Parent; step is not null; step = step.Parent)
        {
            if (ReferenceEquals(step, folder))
            {
                return true;
            }
        }

        return false;
    }
}
