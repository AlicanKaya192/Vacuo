using Vacuo.Core.Disk;

namespace Vacuo.Core.Tests.Disk;

public class DiskTimelineTests
{
    private const long Noticeable = DiskTimeline.NoticeableBytes;
    private const string Downloads = @"C:\Users\Someone\Downloads";
    private const string Videos = @"C:\Users\Someone\Videos";
    private const string Music = @"C:\Users\Someone\Music";
    private const string Pictures = @"C:\Users\Someone\Pictures";
    private const string Şarkılar = @"C:\Users\Someone\Şarkılar";

    private static readonly DateOnly Today = new(2026, 10, 30);

    private static DateOnly DaysAgo(int days) => Today.AddDays(-days);

    private static DiskSnapshot Snapshot(DateOnly day, long usedBytes, params (string Path, long Bytes)[] folders) =>
        new(day, usedBytes, folders.Select(folder => new FolderBytes(folder.Path, folder.Bytes)).ToList());

    private static DiskTimeline Timeline(params DiskSnapshot[] snapshots) =>
        DiskTimeline.From(new DiskHistory(snapshots), Today)!;

    [Fact]
    public void NoHistory_HasNoTimeline()
    {
        Assert.Null(DiskTimeline.From(DiskHistory.Empty, Today));
    }

    [Fact]
    public void OneDayOfHistory_HasNoTimeline()
    {
        Assert.Null(DiskTimeline.From(new DiskHistory([Snapshot(Today, 100)]), Today));
    }

    [Fact]
    public void ConsecutiveDays_MakeAOneDayStep()
    {
        DiskTimeline timeline = Timeline(Snapshot(DaysAgo(1), 1000), Snapshot(Today, 1000 + 2 * Noticeable));

        DiskTimelineStep step = Assert.Single(timeline.Steps);
        Assert.True(step.IsOneDay);
        Assert.Equal(DaysAgo(1), step.From);
        Assert.Equal(Today, step.To);
        Assert.Equal(2 * Noticeable, step.ChangeBytes);
    }

    [Fact]
    public void DaysWithoutAReading_MakeOneStepWithBothDates()
    {
        DiskTimeline timeline = Timeline(Snapshot(DaysAgo(3), 1000), Snapshot(Today, 2000));

        DiskTimelineStep step = Assert.Single(timeline.Steps);
        Assert.False(step.IsOneDay);
        Assert.Equal(DaysAgo(3), step.From);
        Assert.Equal(Today, step.To);
    }

    [Fact]
    public void Steps_AreNewestFirst_AndTheTotalSpansThemAll()
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(5), 1000),
            Snapshot(DaysAgo(2), 1500),
            Snapshot(Today, 1200));

        Assert.Equal([Today, DaysAgo(2)], timeline.Steps.Select(step => step.To));
        Assert.Equal(200, timeline.TotalBytes);
        Assert.Equal(5, timeline.Days);
    }

    [Fact]
    public void ReadingsAreOrderedEvenIfTheFileIsNot()
    {
        DiskTimeline timeline = Timeline(Snapshot(Today, 1500), Snapshot(DaysAgo(2), 1000));

        Assert.Equal(500, Assert.Single(timeline.Steps).ChangeBytes);
    }

    [Fact]
    public void AnOlderOrFutureReading_DoesNotCount()
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(DiskHistory.DaysKept + 1), 1),
            Snapshot(DaysAgo(DiskHistory.DaysKept), 1000),
            Snapshot(Today, 1500),
            Snapshot(Today.AddDays(1), 9999));

        Assert.Equal(500, Assert.Single(timeline.Steps).ChangeBytes);
        Assert.Equal(DiskHistory.DaysKept, timeline.Days);
    }

    [Fact]
    public void TwoReadingsOfADay_CountAsTheLaterOne()
    {
        DiskTimeline timeline = Timeline(Snapshot(DaysAgo(1), 1000), Snapshot(Today, 1100), Snapshot(Today, 1400));

        Assert.Equal(400, Assert.Single(timeline.Steps).ChangeBytes);
    }

    [Fact]
    public void TheLargestStep_IsTheOneThatGrewMost()
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(3), 0),
            Snapshot(DaysAgo(2), 2 * Noticeable),
            Snapshot(DaysAgo(1), 7 * Noticeable),
            Snapshot(Today, 8 * Noticeable));

        Assert.Equal(DaysAgo(1), timeline.Largest!.To);
    }

    [Fact]
    public void OnlyShrinkingSteps_HaveNoLargest()
    {
        DiskTimeline timeline = Timeline(Snapshot(DaysAgo(2), 9 * Noticeable), Snapshot(DaysAgo(1), 5 * Noticeable), Snapshot(Today, Noticeable));

        Assert.Null(timeline.Largest);
        Assert.Equal(-8 * Noticeable, timeline.TotalBytes);
    }

    [Fact]
    public void GrowthBelowTheLimit_IsNoLargest()
    {
        DiskTimeline timeline = Timeline(Snapshot(DaysAgo(1), 0), Snapshot(Today, Noticeable - 1));

        Assert.Null(timeline.Largest);
    }

    [Fact]
    public void AFolderNotThereBefore_GrewFromNothing()
    {
        DiskTimeline timeline = Timeline(Snapshot(DaysAgo(1), 0), Snapshot(Today, 2 * Noticeable, (Downloads, 2 * Noticeable)));

        FolderChange grew = Assert.Single(Assert.Single(timeline.Steps).Grew);
        Assert.Equal(Downloads, grew.Path);
        Assert.Equal(2 * Noticeable, grew.Bytes);
    }

    [Fact]
    public void AFolderNoLongerThere_ShrankToNothing()
    {
        DiskTimeline timeline = Timeline(Snapshot(DaysAgo(1), 2 * Noticeable, (Downloads, 2 * Noticeable)), Snapshot(Today, 0));

        FolderChange? shrank = Assert.Single(timeline.Steps).Shrank;
        Assert.Equal(Downloads, shrank!.Path);
        Assert.Equal(-2 * Noticeable, shrank.Bytes);
    }

    [Fact]
    public void TheLimit_KeepsExactly100MegabytesAndDropsLess()
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(1), 0, (Downloads, 0), (Videos, 0)),
            Snapshot(Today, 2 * Noticeable, (Downloads, Noticeable), (Videos, Noticeable - 1)));

        Assert.Equal(Downloads, Assert.Single(Assert.Single(timeline.Steps).Grew).Path);
    }

    [Fact]
    public void TheLimit_Is100Megabytes()
    {
        Assert.Equal(100L * 1024 * 1024, DiskTimeline.NoticeableBytes);
    }

    [Fact]
    public void AtMostThreeFoldersGrew_AndTheLargestComeFirst()
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(1), 0),
            Snapshot(Today, 0, (Downloads, 2 * Noticeable), (Videos, 5 * Noticeable), (Music, 3 * Noticeable), (Pictures, 4 * Noticeable)));

        Assert.Equal([Videos, Pictures, Music], Assert.Single(timeline.Steps).Grew.Select(folder => folder.Path));
    }

    [Fact]
    public void OnlyTheFolderThatShrankMost_IsNamed()
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(1), 0, (Downloads, 2 * Noticeable), (Videos, 5 * Noticeable)),
            Snapshot(Today, 0, (Downloads, 0), (Videos, 0)));

        Assert.Equal(Videos, Assert.Single(timeline.Steps).Shrank!.Path);
    }

    [Fact]
    public void ASmallShrink_IsNotNamed()
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(1), 0, (Downloads, Noticeable)),
            Snapshot(Today, 0, (Downloads, 1)));

        Assert.Null(Assert.Single(timeline.Steps).Shrank);
    }

    [Fact]
    public void WhatTheFoldersDoNotExplain_IsOutside()
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(1), 1000, (Downloads, 100)),
            Snapshot(Today, 1000 + 500, (Downloads, 100 + 200)));

        Assert.Equal(300, Assert.Single(timeline.Steps).OutsideBytes);
    }

    [Fact]
    public void FoldersBelowTheLimit_StillCountTowardWhatTheFoldersExplain()
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(1), 0, (Downloads, 0)),
            Snapshot(Today, 500, (Downloads, 500)));

        Assert.Equal(0, Assert.Single(timeline.Steps).OutsideBytes);
    }

    [Fact]
    public void PathsDifferingOnlyInCase_AreTheSameFolder()
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(1), 0, (Downloads, 2 * Noticeable)),
            Snapshot(Today, 2 * Noticeable, (Downloads.ToUpperInvariant(), 2 * Noticeable)));

        DiskTimelineStep step = Assert.Single(timeline.Steps);
        Assert.Empty(step.Grew);
        Assert.Null(step.Shrank);
    }

    [Fact]
    public void TurkishFolderNames_AreKept()
    {
        DiskTimeline timeline = Timeline(Snapshot(DaysAgo(1), 0), Snapshot(Today, 0, (Şarkılar, Noticeable)));

        Assert.Equal(Şarkılar, Assert.Single(Assert.Single(timeline.Steps).Grew).Path);
    }

    [Fact]
    public void AQuietStep_HasNothingToSay()
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(1), 1000, (Downloads, 100)),
            Snapshot(Today, 1500, (Downloads, 150)));

        Assert.False(Assert.Single(timeline.Steps).IsNoticeable);
    }

    [Theory]
    [InlineData(Noticeable, 0, 0)]
    [InlineData(-Noticeable, 0, 0)]
    [InlineData(Noticeable, 0, Noticeable)]
    [InlineData(0, Noticeable, 0)]
    [InlineData(0, 0, Noticeable)]
    public void AStepWithAnyNoticeableChange_IsNoticeable(long usedChange, long grew, long shrank)
    {
        DiskTimeline timeline = Timeline(
            Snapshot(DaysAgo(1), Noticeable, (Downloads, 0), (Videos, Noticeable)),
            Snapshot(Today, Noticeable + usedChange + (grew - shrank), (Downloads, grew), (Videos, Noticeable - shrank)));

        Assert.True(Assert.Single(timeline.Steps).IsNoticeable);
    }
}
