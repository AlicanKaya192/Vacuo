using Vacuo.Core.Journal;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Actions;

/// <summary>
/// The only way actions run: check that nothing is in use, measure, act, then log it so the
/// user can undo it later from the journal.
/// </summary>
public sealed class ActionRunner(ActionJournal journal, IRecycleBin recycleBin, TimeProvider time)
{
    public JournalEntry Run(IAction action)
    {
        OpenFileProbe.EnsureNothingInUse(action.TargetPath);
        long sizeBytes = MeasureBytes(action.TargetPath);

        UndoRecord undo = action.Execute();

        var entry = new JournalEntry(Guid.NewGuid(), time.GetUtcNow(), sizeBytes, undo, UndoneAt: null);
        journal.Append(entry);
        return entry;
    }

    public void Undo(Guid entryId)
    {
        JournalEntry entry = journal.ReadAll().SingleOrDefault(entry => entry.Id == entryId)
            ?? throw new ActionRefusedException($"There is no journal entry {entryId}.");
        if (entry.UndoneAt is not null)
        {
            throw new ActionRefusedException($"Journal entry {entryId} was already undone.");
        }

        if (Path.Exists(entry.Undo.OriginalPath))
        {
            throw new ActionRefusedException($"'{entry.Undo.OriginalPath}' is taken again, so nothing was put back.");
        }

        Reverse(entry.Undo);
        journal.MarkUndone(entryId, time.GetUtcNow());
    }

    private void Reverse(UndoRecord undo)
    {
        switch (undo)
        {
            case MovedItem moved:
                OpenFileProbe.EnsureNothingInUse(moved.MovedTo);
                SameVolumeMove.Move(moved.MovedTo, moved.OriginalPath);
                break;
            case RecycledItem recycled:
                recycleBin.Restore(recycled);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(undo), undo, "Unknown kind of undo record.");
        }
    }

    private static long MeasureBytes(string path) =>
        Directory.Exists(path) ? DirectorySizer.Measure(new DirectoryInfo(path)).Bytes : new FileInfo(path).Length;
}
