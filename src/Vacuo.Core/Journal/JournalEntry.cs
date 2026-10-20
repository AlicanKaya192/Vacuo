using Vacuo.Core.Actions;

namespace Vacuo.Core.Journal;

/// <summary>
/// One action in the log: when it ran, how much it moved, how to undo it, and when it was
/// undone (<see langword="null"/> while it still stands). Entries of one Düzen run share a
/// <paramref name="BatchId"/> so they can be undone together; older lines have none.
/// </summary>
public sealed record JournalEntry(Guid Id, DateTimeOffset At, long SizeBytes, UndoRecord Undo, DateTimeOffset? UndoneAt, Guid? BatchId = null);
