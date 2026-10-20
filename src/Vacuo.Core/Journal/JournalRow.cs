using Vacuo.Core.Actions;

namespace Vacuo.Core.Journal;

/// <summary>
/// One line of the Journal screen: a single entry, or a whole Düzen run shown and undone together
/// (B-161). A run counts as undone only once every file in it is back.
/// </summary>
public sealed record JournalRow(IReadOnlyList<JournalEntry> Entries)
{
    public JournalEntry First => Entries[0];

    public Guid? BatchId => First.BatchId;

    public DateTimeOffset At => Entries.Min(entry => entry.At);

    public int FileCount => Files.Count();

    public long SizeBytes => Entries.Sum(entry => entry.SizeBytes);

    /// <summary>The folder a Düzen run tidied; for a single entry, where its item was.</summary>
    public string Folder => Path.GetDirectoryName(First.Undo.OriginalPath) ?? First.Undo.OriginalPath;

    public DateTimeOffset? UndoneAt => BatchId is null
        ? First.UndoneAt
        : Files.All(entry => entry.UndoneAt is not null) ? Files.Max(entry => entry.UndoneAt) : null;

    /// <summary>
    /// Which screen the run came from; <see langword="null"/> for a single entry. Lines written before
    /// batches carried their kind are told apart by what they did: only Düzen moved files.
    /// </summary>
    public JournalBatchKind? BatchKind => BatchId is null
        ? null
        : First.BatchKind ?? (Entries.All(entry => entry.Undo is RecycledItem) ? JournalBatchKind.Duplicates : JournalBatchKind.Organize);

    /// <summary>Every folder the row's items came from, in journal order.</summary>
    public IReadOnlyList<string> Folders => Files
        .Select(entry => Path.GetDirectoryName(entry.Undo.OriginalPath) ?? entry.Undo.OriginalPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private IEnumerable<JournalEntry> Files => Entries.Where(entry => entry.Undo is not CreatedFolder);

    public static IReadOnlyList<JournalRow> From(IEnumerable<JournalEntry> entries) => entries
        .GroupBy(entry => entry.BatchId ?? entry.Id)
        .Select(group => new JournalRow(group.ToList()))
        .OrderByDescending(row => row.At)
        .ToList();
}
