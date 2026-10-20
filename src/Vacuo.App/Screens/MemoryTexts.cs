using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Ram;

namespace Vacuo.App.Screens;

/// <summary>Resource keys for what the Memory card and screen say; the texts live in the .resw files.</summary>
internal static class MemoryTexts
{
    public static string PressureName(Localizer localizer, MemoryPressure pressure) => localizer.Get($"Pressure_{pressure}");

    public static string PressureHint(Localizer localizer, MemoryPressure pressure) => localizer.Get($"PressureHint_{pressure}");

    public static string GroupName(Localizer localizer, AppGroup group) => localizer.Get($"MemoryGroup_{group}");

    public static string GroupHint(Localizer localizer, AppGroup group) => localizer.Get($"MemoryGroupHint_{group}");

    // Calm is the accent colour; only "tight" warns.
    public static Brush PressureBrush(MemoryPressure pressure) =>
        (Brush)Application.Current.Resources[pressure == MemoryPressure.Tight ? "WarningBrush" : "AccentBrush"];

    public static string Available(Localizer localizer, MemorySnapshot snapshot) => localizer.Format(
        "MemoryAvailableFormat",
        DiskTexts.Size(localizer, snapshot.AvailablePhysicalBytes),
        DiskTexts.Size(localizer, snapshot.TotalPhysicalBytes));

    public static string Committed(Localizer localizer, MemorySnapshot snapshot) => localizer.Format(
        "MemoryCommitFormat",
        DiskTexts.Size(localizer, snapshot.CommittedBytes),
        DiskTexts.Size(localizer, snapshot.CommitLimitBytes));

    /// <summary>"+1,2 GB" or "−0,9 GB": a measured change, whichever way it went.</summary>
    public static string SignedSize(Localizer localizer, long bytes) =>
        (bytes < 0 ? "−" : "+") + DiskTexts.Size(localizer, Math.Abs(bytes));

    /// <summary>"Last in front: 2 h 5 min ago", or why that is not known.</summary>
    public static string Usage(Localizer localizer, AppUse use, bool historyKnown)
    {
        if (!historyKnown)
        {
            return string.Empty;
        }

        if (use.UnusedFor is not { } unusedFor)
        {
            return localizer.Get("MemoryInFrontNow");
        }

        return use.SeenInFront
            ? localizer.Format("MemoryLastInFrontFormat", Duration(localizer, unusedFor))
            : localizer.Get("MemoryNeverInFront");
    }

    public static string Duration(Localizer localizer, TimeSpan span) => span switch
    {
        { TotalDays: >= 1 } => localizer.Format("DurationDaysFormat", (int)span.TotalDays, span.Hours),
        { TotalHours: >= 1 } => localizer.Format("DurationHoursFormat", (int)span.TotalHours, span.Minutes),
        _ => localizer.Format("DurationMinutesFormat", Math.Max(0, (int)span.TotalMinutes)),
    };
}
