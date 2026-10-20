using Vacuo.Core.Actions;

namespace Vacuo.Core.Journal;

/// <summary>Why an entry was not put back; the interface turns it into a sentence in either language.</summary>
public enum UndoFailureReason
{
    AlreadyUndone,
    PathTaken,
    FolderGone,
    NotInBin,
    InUse,
    CannotBeUndone,
    Refused,
    FolderNotEmpty,
}

public sealed record BatchUndoFailure(JournalEntry Entry, UndoFailureReason Reason);

/// <summary>
/// The Journal screen without its looks: every entry newest first, and "Undo", which puts an
/// item back only where nothing else stands now. Entries are never removed, only marked undone.
/// </summary>
public sealed class JournalScreen(ActionJournal journal, ActionRunner runner)
{
    public IReadOnlyList<JournalEntry> Entries => journal.ReadAll().OrderByDescending(entry => entry.At).ToList();

    /// <summary>
    /// Undoes a whole Düzen run: files go back newest first, then the folders it made are removed if
    /// they are empty. One failure does not stop the rest; the list is empty when everything is back.
    /// </summary>
    public IReadOnlyList<BatchUndoFailure> UndoBatch(Guid batchId)
    {
        IEnumerable<JournalEntry> entries = journal.ReadAll()
            .Where(entry => entry.BatchId == batchId && entry.UndoneAt is null)
            .Reverse()
            .OrderBy(entry => entry.Undo is CreatedFolder);

        var failures = new List<BatchUndoFailure>();
        foreach (JournalEntry entry in entries)
        {
            if (Undo(entry.Id) is { } reason)
            {
                failures.Add(new BatchUndoFailure(entry, reason));
            }
        }

        return failures;
    }

    /// <summary>Puts the entry's item back. Returns <see langword="null"/> when it is back, else why not.</summary>
    public UndoFailureReason? Undo(Guid entryId)
    {
        JournalEntry entry = journal.ReadAll().Single(entry => entry.Id == entryId);
        string originalPath = entry.Undo.OriginalPath;
        if (entry.UndoneAt is not null)
        {
            return UndoFailureReason.AlreadyUndone;
        }

        if (entry.Undo is DeletedForGood)
        {
            return UndoFailureReason.CannotBeUndone;
        }

        if (entry.Undo is CreatedFolder)
        {
            return UndoCreatedFolder(entryId);
        }

        if (Path.Exists(originalPath))
        {
            return UndoFailureReason.PathTaken;
        }

        if (!Directory.Exists(Path.GetDirectoryName(originalPath)))
        {
            return UndoFailureReason.FolderGone;
        }

        try
        {
            runner.Undo(entryId);
            return null;
        }
        catch (ItemNotInBinException)
        {
            return UndoFailureReason.NotInBin;
        }
        catch (FileInUseException)
        {
            return UndoFailureReason.InUse;
        }
        catch (ActionRefusedException)
        {
            return UndoFailureReason.Refused;
        }
    }

    private UndoFailureReason? UndoCreatedFolder(Guid entryId)
    {
        try
        {
            runner.Undo(entryId);
            return null;
        }
        catch (FolderNotEmptyException)
        {
            return UndoFailureReason.FolderNotEmpty;
        }
        catch (Exception exception) when (exception is ActionRefusedException or IOException or UnauthorizedAccessException)
        {
            return UndoFailureReason.Refused;
        }
    }
}
