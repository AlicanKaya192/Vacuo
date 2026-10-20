namespace Vacuo.Core.Maintenance;

/// <summary>
/// When Otomatik Bakım runs (ADR-004): turned on with something chosen, on mains power, after ten minutes
/// without keyboard or mouse, and at most once a local day. The tray asks every
/// <see cref="CheckInterval"/>; the helper stops as soon as the user is back.
/// </summary>
public static class MaintenancePolicy
{
    public static TimeSpan IdleBeforeRun { get; } = TimeSpan.FromMinutes(10);

    public static TimeSpan CheckInterval { get; } = TimeSpan.FromMinutes(15);

    /// <summary>A last run in the future means the clock was set back; waiting for it could take days.</summary>
    public static bool IsDue(MaintenanceSettings settings, DateTimeOffset? lastRun, TimeSpan idleFor, bool onMains, DateTimeOffset now) =>
        settings is { Enabled: true, HasWork: true }
        && onMains
        && idleFor >= IdleBeforeRun
        && (lastRun is not { } last || last > now || last.ToOffset(now.Offset).Date != now.Date);

    /// <summary>Keyboard or mouse input since the run started.</summary>
    public static bool UserReturned(DateTimeOffset lastInput, DateTimeOffset startedAt) => lastInput > startedAt;
}
