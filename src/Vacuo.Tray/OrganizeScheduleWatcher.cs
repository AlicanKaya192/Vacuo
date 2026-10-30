using System.ComponentModel;
using System.Diagnostics;
using Vacuo.Core.Organize;

namespace Vacuo.Tray;

/// <summary>
/// The tray's part of the organize reminders (Aşama 3m): when one is due and the user is at the computer, it starts
/// the helper to look and ask. The tray reads only the dates; it never lists a folder or moves anything (ADR-004).
/// </summary>
internal sealed class OrganizeScheduleWatcher(string helperPath)
{
    private const string OrganizeSwitch = "--organize";

    // The first look is one interval after the tray starts, so a reminder never joins the crowd at sign-in.
    private long _lastCheckMs = Environment.TickCount64;

    /// <summary>Called on every tray timer tick; reads the file only once per check interval.</summary>
    public void Check()
    {
        long nowMs = Environment.TickCount64;
        if (nowMs - _lastCheckMs < (long)OrganizeSchedulePolicy.CheckInterval.TotalMilliseconds)
        {
            return;
        }

        _lastCheckMs = nowMs;
        if (!File.Exists(helperPath)
            || MaintenanceScheduler.IsHelperRunning()
            || MaintenanceScheduler.IdleFor() > OrganizeSchedulePolicy.AtTheComputerWithin
            || OrganizeSchedulePolicy.Due(new OrganizeScheduleStore(OrganizeScheduleStore.DefaultPath).Load(), DateTimeOffset.Now).Count == 0)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(helperPath, OrganizeSwitch) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            // Blocked or missing: tried again at the next check.
        }
    }
}
