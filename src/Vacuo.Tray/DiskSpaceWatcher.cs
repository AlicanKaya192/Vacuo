using System.ComponentModel;
using System.Diagnostics;
using Vacuo.Core.Notices;
using Vacuo.Core.Settings;

namespace Vacuo.Tray;

/// <summary>
/// The tray's low-disk check (Aşama 3g): the system drive's free space, one cheap Windows call every
/// <see cref="DiskSpaceNoticePolicy.CheckInterval"/>. When the drive becomes almost full and the notice is on
/// in Settings, it starts the helper to show it; the tray draws nothing and scans nothing (ADR-004).
/// </summary>
internal sealed class DiskSpaceWatcher(string helperPath)
{
    private const string DiskSwitch = "--disk";

    private bool _wasLow;
    private DateTimeOffset? _lastCheck;
    private DateTimeOffset? _lastNotice;

    /// <summary>Called on every tray timer tick; reads the drive only once per check interval.</summary>
    public void Check()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        if (_lastCheck is { } last && last <= now && now - last < DiskSpaceNoticePolicy.CheckInterval)
        {
            return;
        }

        _lastCheck = now;
        if (SystemDrive.Read() is not { } drive)
        {
            return;
        }

        bool isLow = DiskSpaceNoticePolicy.IsLow(drive.AvailableFreeSpace, drive.TotalSize);
        bool tell = DiskSpaceNoticePolicy.ShouldNotify(isLow, _wasLow, _lastNotice, now);
        _wasLow = isLow;
        if (!tell || !File.Exists(helperPath) || !new SettingsStore(SettingsStore.DefaultPath).Load().LowDiskNotice)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(helperPath, DiskSwitch) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            _lastNotice = now;
        }
        catch (Win32Exception)
        {
            // Blocked or missing: told the next time the drive becomes almost full.
        }
    }
}
