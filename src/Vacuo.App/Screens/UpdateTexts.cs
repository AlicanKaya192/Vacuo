using Vacuo.App.Localization;
using Vacuo.App.Updates;
using Vacuo.Core.Formatting;
using Vacuo.Core.Updates;
using Vacuo.Windows;

namespace Vacuo.App.Screens;

/// <summary>The update texts shared by the header, Settings and the update screen.</summary>
internal static class UpdateTexts
{
    private const string Separator = " · ";

    public static int Percent(DownloadProgress progress) =>
        progress.Total <= 0 ? 0 : (int)Math.Min(100, progress.Received * 100 / progress.Total);

    /// <summary>"28,1 MB / 65,8 MB · 3,2 MB/sn · about 12 s left".</summary>
    public static string Download(Localizer localizer, UpdateSession updates)
    {
        var parts = new List<string>
        {
            $"{Size(localizer, updates.Progress.Received)} / {Size(localizer, updates.Progress.Total)}",
        };
        if (updates.BytesPerSecond is > 0 and { } rate)
        {
            parts.Add(localizer.Format("UpdateSpeedFormat", Size(localizer, (long)rate)));
        }

        parts.Add(Remaining(localizer, updates.Remaining));
        return string.Join(Separator, parts);
    }

    public static string Remaining(Localizer localizer, TimeSpan? remaining)
    {
        if (remaining is not { } left)
        {
            return localizer.Get("UpdateRemainingUnknown");
        }

        TimeSpan shown = TransferMeter.RoundForDisplay(left);
        return shown.TotalSeconds <= 60
            ? localizer.Format("UpdateRemainingSecondsFormat", (int)shown.TotalSeconds)
            : localizer.Format("UpdateRemainingMinutesFormat", (int)shown.TotalMinutes);
    }

    public static string? Problem(Localizer localizer, UpdateProblem problem) => problem switch
    {
        UpdateProblem.Cancelled => localizer.Get("UpdateCancelled"),
        UpdateProblem.Offline => localizer.Get("UpdateOffline"),
        UpdateProblem.Failed => localizer.Get("UpdateFailed"),
        UpdateProblem.Rejected => localizer.Get("UpdateRejected"),
        UpdateProblem.Blocked => localizer.Get("UpdateBlocked"),
        UpdateProblem.InstallFailed => localizer.Get("UpdateInstallFailed"),
        _ => null,
    };

    /// <summary>Settings' line under "Updates": the running version and what the last check found.</summary>
    public static string CheckState(Localizer localizer, UpdateSession updates)
    {
        string version = localizer.Format("SettingsUpdatesVersionFormat", updates.Running);
        string state = updates switch
        {
            { Phase: UpdatePhase.Checking } => localizer.Get("SettingsUpdatesChecking"),
            { Phase: UpdatePhase.Downloading or UpdatePhase.Installing } => localizer.Format("UpdateBadgeDownloadingFormat", Percent(updates.Progress)),
            { Offer: { } offer } => localizer.Format("SettingsUpdatesAvailableFormat", offer.Version),
            { LastStatus: UpdateStatus.Current } => localizer.Get("SettingsUpdatesCurrent"),
            { LastStatus: UpdateStatus.Offline } => localizer.Get("SettingsUpdatesOffline"),
            { LastStatus: UpdateStatus.Failed } => localizer.Get("SettingsUpdatesFailed"),
            { LastCheckedAt: { } at } => localizer.Format("SettingsUpdatesLastCheckFormat", at.ToLocalTime().ToString("g", localizer.Culture)),
            _ => localizer.Get("SettingsUpdatesNever"),
        };
        return version + Separator + state;
    }

    public static string Size(Localizer localizer, long bytes) => ByteSizeFormatter.Format(bytes, localizer.Culture);
}
