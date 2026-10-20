using Vacuo.Core.Actions;

namespace Vacuo.Core.Journal;

/// <summary>Which screen a batch came from, so the Journal can name it.</summary>
public enum JournalBatchKind
{
    Organize,
    Duplicates,
    Leftovers,
    Maintenance,
}

/// <summary>
/// One action in the log: when it ran, how much it moved, how to undo it, and when it was
/// undone (<see langword="null"/> while it still stands). Entries of one run share a
/// <paramref name="BatchId"/> so they can be undone together, and say which screen made it in
/// <paramref name="BatchKind"/>; older lines have neither.
/// </summary>
public sealed record JournalEntry(
    Guid Id,
    DateTimeOffset At,
    long SizeBytes,
    UndoRecord Undo,
    DateTimeOffset? UndoneAt,
    Guid? BatchId = null,
    JournalBatchKind? BatchKind = null);
