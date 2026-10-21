using Vacuo.Core.Actions;
using Vacuo.Core.Journal;

namespace Vacuo.Core.Tests.Journal;

public class WeekSummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 15, 0, 0, TimeSpan.FromHours(3));
    private const string Bin = @"C:\$Recycle.Bin\x";

    private static JournalEntry Recycled(long bytes, DateTimeOffset at, Guid? batch = null, JournalBatchKind? kind = null, DateTimeOffset? undoneAt = null) =>
        new(Guid.NewGuid(), at, bytes, new RecycledItem(@"C:\Users\test\cache", at, Bin), undoneAt, batch, kind);

    private static JournalEntry Moved(DateTimeOffset at, Guid batch) =>
        new(Guid.NewGuid(), at, 50, new MovedItem(@"C:\Users\test\Desktop\a.pdf", @"C:\Users\test\Desktop\Belgeler\a.pdf"), null, batch, JournalBatchKind.Organize);

    private static JournalEntry Folder(DateTimeOffset at, Guid batch) =>
        new(Guid.NewGuid(), at, 0, new CreatedFolder(@"C:\Users\test\Desktop\Belgeler"), null, batch, JournalBatchKind.Organize);

    private static JournalEntry DeletedForGood(long bytes, DateTimeOffset at) =>
        new(Guid.NewGuid(), at, bytes, new DeletedForGood(@"C:\Windows\SoftwareDistribution\Download"), null);

    [Fact]
    public void NothingDone_HasNoCards()
    {
        WeekSummary week = WeekSummary.From([], Now);

        Assert.Empty(week.Cards);
        Assert.Equal(0, week.InBinBytes);
        Assert.Equal(0, week.DeletedBytes);
    }

    [Fact]
    public void OnlyTheLastSevenDaysCount()
    {
        WeekSummary week = WeekSummary.From([Recycled(100, Now.AddDays(-6.9)), Recycled(900, Now.AddDays(-7.1))], Now);

        WeekCard card = Assert.Single(week.Cards);
        Assert.Equal(100, card.Bytes);
        Assert.Equal(100, week.InBinBytes);
    }

    [Fact]
    public void WhatWasPutBack_DoesNotCount()
    {
        WeekSummary week = WeekSummary.From([Recycled(100, Now.AddDays(-1), undoneAt: Now.AddHours(-2))], Now);

        Assert.Empty(week.Cards);
    }

    [Fact]
    public void ASingleItemFromTheDiskScreen_IsADiskCard()
    {
        WeekCard card = Assert.Single(WeekSummary.From([Recycled(300, Now.AddDays(-2)), Recycled(200, Now.AddDays(-1))], Now).Cards);

        Assert.Equal(WeekSource.Disk, card.Source);
        Assert.Equal(2, card.Items);
        Assert.Equal(500, card.Bytes);
        Assert.Equal(Now.AddDays(-1), card.Last);
    }

    [Fact]
    public void RunsAreCountedByTheScreenThatMadeThem()
    {
        var maintenance = Guid.NewGuid();
        var duplicates = Guid.NewGuid();
        WeekSummary week = WeekSummary.From(
        [
            Recycled(1000, Now.AddDays(-1), maintenance, JournalBatchKind.Maintenance),
            Recycled(1000, Now.AddDays(-1), maintenance, JournalBatchKind.Maintenance),
            Recycled(500, Now.AddDays(-3), duplicates, JournalBatchKind.Duplicates),
        ], Now);

        Assert.Equal([WeekSource.Maintenance, WeekSource.Duplicates], week.Cards.Select(card => card.Source));
        Assert.Equal(2, week.Cards[0].Items);
        Assert.Equal(2500, week.InBinBytes);
    }

    // Düzen moves files within a folder: it frees nothing, and the folders it made are not files.
    [Fact]
    public void Organize_CountsFilesButFreesNothing()
    {
        var run = Guid.NewGuid();
        WeekCard card = Assert.Single(WeekSummary.From([Folder(Now.AddDays(-1), run), Moved(Now.AddDays(-1), run), Moved(Now.AddDays(-1), run)], Now).Cards);

        Assert.Equal(WeekSource.Organize, card.Source);
        Assert.Equal(2, card.Items);
        Assert.Equal(0, card.Bytes);
    }

    // Only the Windows leftovers that need an administrator are deleted for good (ADR-003).
    [Fact]
    public void DeletedForGood_IsWindowsLeftoversAndFreedAtOnce()
    {
        WeekSummary week = WeekSummary.From([DeletedForGood(4000, Now.AddDays(-1)), Recycled(100, Now.AddDays(-1))], Now);

        Assert.Equal(WeekSource.Windows, week.Cards[0].Source);
        Assert.Equal(4000, week.DeletedBytes);
        Assert.Equal(100, week.InBinBytes);
    }

    [Fact]
    public void Cards_GoFromMostFreedToLeast()
    {
        var organize = Guid.NewGuid();
        WeekSummary week = WeekSummary.From(
            [Recycled(10, Now.AddDays(-1)), Moved(Now.AddDays(-1), organize), Recycled(5000, Now.AddDays(-2), Guid.NewGuid(), JournalBatchKind.Leftovers)],
            Now);

        Assert.Equal([WeekSource.Uninstall, WeekSource.Disk, WeekSource.Organize], week.Cards.Select(card => card.Source));
    }
}
