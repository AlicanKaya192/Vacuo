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
    public JournalEntry RecordCreatedFolder(string path, Guid batchId, FolderIcon? icon = null)
    {
        var entry = new JournalEntry(Guid.NewGuid(), time.GetUtcNow(), SizeBytes: 0, new CreatedFolder(path, icon), UndoneAt: null, batchId);
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
                RemoveIfEmpty(created);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(undo), undo, "Unknown kind of undo record.");
        }
    }

    // Only the folder Vacuo made, and only while nothing is inside but its own unchanged icon files;
    // anything put there since, a changed icon file too, keeps the whole folder as it is (B-161, Aşama 3h).
    private static void RemoveIfEmpty(CreatedFolder created)
    {
        string folder = created.OriginalPath;
        if (!Directory.Exists(folder))
        {
            return;
        }

        IReadOnlyList<string> iconFiles = created.Icon?.UnchangedFilesIn(folder) ?? [];
        if (Directory.EnumerateFileSystemEntries(folder).Except(iconFiles, StringComparer.OrdinalIgnoreCase).Any())
        {
            throw new FolderNotEmptyException(folder);
        }

        foreach (string iconFile in iconFiles)
        {
            File.SetAttributes(iconFile, FileAttributes.Normal);
            File.Delete(iconFile);
        }

        // The icon marked the folder read-only, which is how the shell knows to read its desktop.ini.
        var directory = new DirectoryInfo(folder);
        directory.Attributes &= ~FileAttributes.ReadOnly;
        directory.Delete(recursive: false);
    }

    private static long MeasureBytes(string path) =>
        Directory.Exists(path) ? DirectorySizer.Measure(new DirectoryInfo(path)).Bytes : new FileInfo(path).Length;
}
