using Vacuo.Core.Journal;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Actions;

/// <summary>
/// The only way actions run: check that nothing is in use, measure, act, then log it so the
/// user can undo it later from the journal.
/// </summary>
public sealed class ActionRunner(ActionJournal journal, IRecycleBin recycleBin, TimeProvider time)
{
    public JournalEntry Run(IAction action, Guid? batchId = null, JournalBatchKind? batchKind = null)
    {
        OpenFileProbe.EnsureNothingInUse(action.TargetPath);
        long sizeBytes = MeasureBytes(action.TargetPath);

        UndoRecord undo = action.Execute();

        var entry = new JournalEntry(Guid.NewGuid(), time.GetUtcNow(), sizeBytes, undo, UndoneAt: null, batchId, batchKind);
        journal.Append(entry);
        return entry;
    }

    /// <summary>Logs a folder Düzen made, so undoing the batch can remove it again while it is empty.</summary>
    public JournalEntry RecordCreatedFolder(string path, Guid batchId)
    {
        var entry = new JournalEntry(Guid.NewGuid(), time.GetUtcNow(), SizeBytes: 0, new CreatedFolder(path), UndoneAt: null, batchId);
        journal.Append(entry);
        return entry;
    }

    /// <summary>Logs a cleanup the administrator helper did for good; it cannot be undone.</summary>
    public JournalEntry RecordDeletedForGood(string path, long sizeBytes)
    {
        var entry = new JournalEntry(Guid.NewGuid(), time.GetUtcNow(), sizeBytes, new DeletedForGood(path), UndoneAt: null);
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

        if (entry.Undo is not CreatedFolder && Path.Exists(entry.Undo.OriginalPath))
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
            case DeletedForGood deleted:
                throw new ActionRefusedException($"'{deleted.OriginalPath}' was deleted for good and cannot be put back.");
            case CreatedFolder created:
                RemoveIfEmpty(created.OriginalPath);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(undo), undo, "Unknown kind of undo record.");
        }
    }

    // Only the folder Vacuo made, and only while nothing is inside; anything put there since stays (B-161).
    private static void RemoveIfEmpty(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        if (Directory.EnumerateFileSystemEntries(folder).Any())
        {
            throw new FolderNotEmptyException(folder);
        }

        Directory.Delete(folder, recursive: false);
    }

    private static long MeasureBytes(string path) =>
        Directory.Exists(path) ? DirectorySizer.Measure(new DirectoryInfo(path)).Bytes : new FileInfo(path).Length;
}
