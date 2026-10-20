namespace Vacuo.Core.Updates;

/// <summary>
/// When to ask GitHub (vacuo-kurulum-guncelleme-plani): when the panel opens, at most every six hours, and
/// whenever the user presses the button in Settings; never while checking is turned off (vacuo-kurallar 6).
/// The tray never asks, so it stays light while idle.
/// </summary>
public static class UpdateSchedule
{
    public static TimeSpan Interval { get; } = TimeSpan.FromHours(6);

    /// <summary>A last check in the future means the clock was set back; waiting for it could take days.</summary>
    public static bool IsDue(bool enabled, bool byHand, DateTimeOffset? lastCheck, DateTimeOffset now) =>
        enabled && (byHand || lastCheck is not { } last || last > now || now - last >= Interval);

    /// <summary>Only an answer from GitHub counts: without internet the next opening tries again.</summary>
    public static bool CountsAsChecked(UpdateStatus status) => status is UpdateStatus.Newer or UpdateStatus.Current;
}
