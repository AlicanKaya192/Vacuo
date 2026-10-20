using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vacuo.Core.Maintenance;
using Vacuo.Core.Settings;
using static Vacuo.Tray.NativeMethods;

namespace Vacuo.Tray;

/// <summary>
/// The tray's part of Otomatik Bakım (ADR-004): on each check, when it is turned on and due, it starts the
/// helper without a window. The tray itself never scans or moves anything, so it stays light while idle.
/// The settings are read on every check, so a change in Settings counts from the next one.
/// </summary>
internal sealed class MaintenanceScheduler(string helperPath)
{
    // The helper holds this while it runs.
    private const string HelperRunningName = @"Local\Vacuo.Maintenance";

    public void Check()
    {
        MaintenanceSettings settings = new SettingsStore(SettingsStore.DefaultPath).Load().Maintenance;
        if (!settings.Enabled || !File.Exists(helperPath) || IsHelperRunning())
        {
            return;
        }

        DateTimeOffset? lastRun = new MaintenanceStateStore(MaintenanceStateStore.DefaultPath).LoadLastRun();
        if (!MaintenancePolicy.IsDue(settings, lastRun, IdleFor(), OnMains(), DateTimeOffset.Now))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(helperPath) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            // Blocked or missing: tried again at the next check.
        }
    }

    private static bool IsHelperRunning()
    {
        if (!Mutex.TryOpenExisting(HelperRunningName, out Mutex? running))
        {
            return false;
        }

        running.Dispose();
        return true;
    }

    private static TimeSpan IdleFor()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info)
            ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime))
            : TimeSpan.Zero;
    }

    private static bool OnMains() => GetSystemPowerStatus(out SystemPowerStatus status) && status.ACLineStatus == AcOnline;
}
