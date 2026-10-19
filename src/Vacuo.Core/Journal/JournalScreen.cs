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
}

/// <summary>
/// The Journal screen without its looks: every entry newest first, and "Undo", which puts an
/// item back only where nothing else stands now. Entries are never removed, only marked undone.
/// </summary>
public sealed class JournalScreen(ActionJournal journal, ActionRunner runner)
{
    public IReadOnlyList<JournalEntry> Entries => journal.ReadAll().OrderByDescending(entry => entry.At).ToList();

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
}
