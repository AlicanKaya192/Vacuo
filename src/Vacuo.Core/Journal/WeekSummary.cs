using Vacuo.Core.Actions;

namespace Vacuo.Core.Journal;

/// <summary>Which part of Vacuo did the work: the screen a run came from, or the Disk screen for a single item.</summary>
public enum WeekSource
{
    Disk,
    Windows,
    Duplicates,
    Uninstall,
    Maintenance,
    Organize,
}

/// <summary>One card of the week: how many items, how much they freed (nothing for Düzen) and when last.</summary>
public sealed record WeekCard(WeekSource Source, int Items, long Bytes, DateTimeOffset Last);

/// <summary>
/// "Bu hafta neler boşaldı" (Aşama 3d E): the Journal's last <see cref="Window"/>, one card per source, most
/// freed first. What was put back does not count. Space in the Recycle Bin is told apart from space freed at
/// once (only Windows leftovers that need an administrator are deleted for good, ADR-003): the first frees
/// up only when the bin is emptied.
/// </summary>
public sealed record WeekSummary(IReadOnlyList<WeekCard> Cards, long InBinBytes, long DeletedBytes)
{
    public static TimeSpan Window { get; } = TimeSpan.FromDays(7);

    public static WeekSummary From(IEnumerable<JournalEntry> entries, DateTimeOffset now)
    {
        DateTimeOffset since = now - Window;
        List<JournalEntry> standing = entries
            .Where(entry => entry.UndoneAt is null && entry.At > since && entry.At <= now)
            .ToList();

        List<WeekCard> cards = JournalRow.From(standing)
            .SelectMany(row => row.Entries.Where(entry => entry.Undo is not CreatedFolder).Select(entry => (Source: SourceOf(row, entry), Entry: entry)))
            .GroupBy(item => item.Source)
            .Select(group => new WeekCard(
                group.Key,
                group.Count(),
                group.Sum(item => FreedBytes(item.Entry)),
                group.Max(item => item.Entry.At)))
            .OrderByDescending(card => card.Bytes)
            .ThenByDescending(card => card.Items)
            .ToList();

        return new WeekSummary(
            cards,
            standing.Where(entry => entry.Undo is RecycledItem).Sum(entry => entry.SizeBytes),
            standing.Where(entry => entry.Undo is DeletedForGood).Sum(entry => entry.SizeBytes));
    }

    private static WeekSource SourceOf(JournalRow row, JournalEntry entry) => row.BatchKind switch
    {
        JournalBatchKind.Organize => WeekSource.Organize,
        JournalBatchKind.Duplicates => WeekSource.Duplicates,
        JournalBatchKind.Leftovers => WeekSource.Uninstall,
        JournalBatchKind.Maintenance => WeekSource.Maintenance,
        _ => entry.Undo is DeletedForGood ? WeekSource.Windows : WeekSource.Disk,
    };

    // A moved file is still on the same drive.
    private static long FreedBytes(JournalEntry entry) => entry.Undo is RecycledItem or DeletedForGood ? entry.SizeBytes : 0;
}
