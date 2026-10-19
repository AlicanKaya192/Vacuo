using Vacuo.Core.Actions;

namespace Vacuo.Core.Journal;

/// <summary>
/// One action in the log: when it ran, how much it moved, how to undo it, and when it was
/// undone (<see langword="null"/> while it still stands).
/// </summary>
public sealed record JournalEntry(Guid Id, DateTimeOffset At, long SizeBytes, UndoRecord Undo, DateTimeOffset? UndoneAt);
