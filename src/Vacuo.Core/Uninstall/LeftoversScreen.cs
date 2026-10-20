using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;

namespace Vacuo.Core.Uninstall;

/// <summary>
/// The folders a removed program left behind, after <see cref="LeftoverFinder"/> suggested them.
/// Nothing starts picked; the picked folders go to the Recycle Bin as one journal batch, each on
/// its own, so one failure does not stop the others.
/// </summary>
public sealed class LeftoversScreen(IReadOnlyList<LeftoverFolder> folders)
{
    private readonly List<LeftoverFolder> _folders = [.. folders];
    private readonly HashSet<string> _picked = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LeftoverFolder> Folders => _folders;

    public int PickedCount => _picked.Count;

    public long PickedBytes => Picked().Sum(folder => folder.Size.Bytes);

    public bool IsPicked(string path) => _picked.Contains(path);

    public void Pick(string path)
    {
        if (!_folders.Any(folder => string.Equals(folder.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"'{path}' was not suggested as a leftover.", nameof(path));
        }

        _picked.Add(path);
    }

    public void Unpick(string path) => _picked.Remove(path);

    public MoveOutcome MoveSelected(ActionRunner runner, IRecycleBin recycleBin)
    {
        var batchId = Guid.NewGuid();
        var moved = new List<JournalEntry>();
        var failed = new List<MoveFailure>();

        foreach (LeftoverFolder folder in Picked())
        {
            if (!Directory.Exists(folder.Path))
            {
                failed.Add(new MoveFailure(folder.Path, MoveFailureReason.Missing));
                continue;
            }

            try
            {
                moved.Add(runner.Run(new MoveToRecycleBinAction(folder.Path, recycleBin), batchId, JournalBatchKind.Leftovers));
                _folders.Remove(folder);
                _picked.Remove(folder.Path);
            }
            catch (ActionRefusedException refusal)
            {
                failed.Add(new MoveFailure(folder.Path, MoveFailures.ReasonFor(refusal)));
            }
        }

        return new MoveOutcome(moved, failed);
    }

    private List<LeftoverFolder> Picked() => _folders.Where(folder => _picked.Contains(folder.Path)).ToList();
}
