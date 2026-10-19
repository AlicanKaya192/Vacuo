using Vacuo.App.Localization;
using Vacuo.Core.Disk;
using Vacuo.Core.Formatting;
using Vacuo.Core.Scanning;

namespace Vacuo.App.Screens;

/// <summary>Resource keys for the names the Disk card and screen show; the texts live in the .resw files.</summary>
internal static class DiskTexts
{
    public static string BucketName(Localizer localizer, Bucket bucket) => localizer.Get($"Bucket_{bucket}");

    public static string BucketHint(Localizer localizer, Bucket bucket) => localizer.Get($"BucketHint_{bucket}");

    public static string CostName(Localizer localizer, RebuildCost cost) => localizer.Get($"Cost_{cost}");

    // Resource names avoid '-', so rule ids such as "pip-cache" become "Rule_pip_cache".
    public static string RuleName(Localizer localizer, CacheRule rule) => localizer.Get("Rule_" + rule.Id.Replace('-', '_'));

    public static string FailureReason(Localizer localizer, MoveFailureReason reason) => localizer.Get($"Failure_{reason}");

    public static string Size(Localizer localizer, long bytes) => ByteSizeFormatter.Format(bytes, localizer.Culture);
}
