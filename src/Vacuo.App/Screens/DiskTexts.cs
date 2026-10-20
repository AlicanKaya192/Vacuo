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

    // "tools\__pycache__" says more than "__pycache__" alone, and still fits on a small label.
    public static string ShortName(string path) =>
        Path.Join(Path.GetFileName(Path.GetDirectoryName(path)), Path.GetFileName(path));

    public static string Size(Localizer localizer, long bytes) => ByteSizeFormatter.Format(bytes, localizer.Culture);
}
