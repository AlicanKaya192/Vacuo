using Vacuo.App.Localization;
using Vacuo.Core.Disk;
using Vacuo.Core.Formatting;
using Vacuo.Core.Journal;
using Vacuo.Core.Scanning;

namespace Vacuo.App.Screens;

/// <summary>Resource keys for the names the Disk card and screen show; the texts live in the .resw files.</summary>
internal static class DiskTexts
{
    public static string BucketName(Localizer localizer, Bucket bucket) => localizer.Get($"Bucket_{bucket}");

    public static string CategoryName(Localizer localizer, DiskCategory category) => localizer.Get($"Category_{category}");

    // A share under one percent still reads as "<1%", not as nothing.
    public static string Percent(Localizer localizer, double fraction) =>
        fraction is > 0 and < 0.01
            ? localizer.Format("DiskPercentUnderFormat", 0.01.ToString("P0", localizer.Culture))
            : fraction.ToString("P0", localizer.Culture);

    public static string BucketHint(Localizer localizer, Bucket bucket) => localizer.Get($"BucketHint_{bucket}");

    public static string CostName(Localizer localizer, RebuildCost cost) => localizer.Get($"Cost_{cost}");

    // Resource names avoid '-', so rule ids such as "pip-cache" become "Rule_pip_cache".
    public static string RuleName(Localizer localizer, CacheRule rule) => localizer.Get("Rule_" + rule.Id.Replace('-', '_'));

    public static string FailureReason(Localizer localizer, MoveFailureReason reason) => localizer.Get($"Failure_{reason}");

    public static string UndoFailure(Localizer localizer, UndoFailureReason reason) => localizer.Get($"UndoFailure_{reason}");

    public static string ShortName(string path) => PathNames.Short(path);

    public static string Size(Localizer localizer, long bytes) => ByteSizeFormatter.Format(bytes, localizer.Culture);

    /// <summary>The Disk screen's line: how the drive changed and which folders grew most; null until two days compare.</summary>
    public static string Growth(Localizer localizer, DiskGrowth? growth)
    {
        if (growth is null)
        {
            return localizer.Get("DiskGrowthStarting");
        }

        string period = GrowthPeriod(localizer, growth.Days, growth.ChangeBytes);
        if (growth.Folders.Count == 0)
        {
            return period + ".";
        }

        IEnumerable<string> folders = growth.Folders.Select(folder =>
            $"{ShortName(folder.Path)} ({ByteSizeFormatter.FormatChange(folder.GrownBytes, localizer.Culture)})");
        return localizer.Format("DiskGrowthWithFoldersFormat", period, string.Join(", ", folders));
    }

    private static string GrowthPeriod(Localizer localizer, int days, long changeBytes)
    {
        string change = Change(localizer, changeBytes);
        return days == 1
            ? localizer.Format("DiskGrowthOneDayFormat", change)
            : localizer.Format("DiskGrowthDaysFormat", days, change);
    }

    private static string Change(Localizer localizer, long bytes) => ByteSizeFormatter.FormatChange(bytes, localizer.Culture);

    /// <summary>"The period it grew most: October 27–30 (+4.1 GB)"; only when a step grew noticeably.</summary>
    public static string TimelineLargest(Localizer localizer, DiskTimeline timeline) => timeline.Largest is { } largest
        ? localizer.Format("DiskTimelineLargestFormat", TimelineDates(localizer, largest), Change(localizer, largest.ChangeBytes))
        : string.Empty;

    public static string TimelineTotal(Localizer localizer, DiskTimeline timeline) =>
        GrowthPeriod(localizer, timeline.Days, timeline.TotalBytes);

    /// <summary>A day alone is that day; a step over several days says only that it happened between them.</summary>
    public static string TimelinePeriod(Localizer localizer, DiskTimelineStep step) => step.IsOneDay
        ? TimelineDates(localizer, step)
        : localizer.Format("DiskTimelineBetweenFormat", TimelineDates(localizer, step));

    public static string TimelineChange(Localizer localizer, DiskTimelineStep step) => step.IsNoticeable
        ? Change(localizer, step.ChangeBytes)
        : localizer.Get("DiskTimelineQuiet");

    /// <summary>The folders that grew on one line; what shrank and what no known folder explains on the next.</summary>
    public static IEnumerable<string> TimelineFolders(Localizer localizer, DiskTimelineStep step)
    {
        if (step.Grew.Count > 0)
        {
            yield return string.Join(" · ", step.Grew.Select(folder => FolderChangeText(localizer, folder)));
        }

        var others = new List<string>();
        if (step.Shrank is { } shrank)
        {
            others.Add(localizer.Format("DiskTimelineShrankFormat", FolderChangeText(localizer, shrank)));
        }

        if (Math.Abs(step.OutsideBytes) >= DiskTimeline.NoticeableBytes)
        {
            others.Add(localizer.Format("DiskTimelineOutsideFormat", Change(localizer, step.OutsideBytes)));
        }

        if (others.Count > 0)
        {
            yield return string.Join(" · ", others);
        }
    }

    private static string FolderChangeText(Localizer localizer, FolderChange folder) =>
        $"{ShortName(folder.Path)} {Change(localizer, folder.Bytes)}";

    // "27–30 Ekim" and "October 27–30" put the month on opposite ends, so the day and month forms come from the .resw.
    private static string TimelineDates(Localizer localizer, DiskTimelineStep step)
    {
        string day = localizer.Get("DiskTimelineDayFormat");
        if (step.IsOneDay)
        {
            return step.To.ToString(day, localizer.Culture);
        }

        return step.From.Month == step.To.Month && step.From.Year == step.To.Year
            ? step.From.ToString(localizer.Get("DiskTimelineRangeFromFormat"), localizer.Culture)
                + "–"
                + step.To.ToString(localizer.Get("DiskTimelineRangeToFormat"), localizer.Culture)
            : step.From.ToString(day, localizer.Culture) + " – " + step.To.ToString(day, localizer.Culture);
    }

    /// <summary>What a move ended with, in the same words the screens show: how much was moved, how many could not be.</summary>
    /// <summary>The line under a move's total: what took the most of it; null when the total already says it.</summary>
    public static string? Largest(Localizer localizer, MoveOutcome outcome) => outcome.Largest is { } largest
        ? localizer.Format("DiskMovedLargestFormat", ShortName(largest.Path), Size(localizer, largest.Bytes), largest.Percent)
        : null;

    public static string Outcome(Localizer localizer, MoveOutcome outcome)
    {
        var parts = new List<string>();
        if (outcome.Moved.Count > 0)
        {
            parts.Add(localizer.Format("DiskMovedFormat", Size(localizer, outcome.FreedBytes)));
        }

        if (Largest(localizer, outcome) is { } largest)
        {
            parts.Add(largest);
        }

        if (outcome.Failed.Count > 0)
        {
            parts.Add(localizer.Format("DiskFailedFormat", outcome.Failed.Count));
        }

        return string.Join(". ", parts);
    }

    /// <summary>A screen reader's name for a list row's check box: what the row is, where, and how big.</summary>
    public static string RowName(params string[] parts) => string.Join(", ", parts.Where(part => part.Length > 0));
}
