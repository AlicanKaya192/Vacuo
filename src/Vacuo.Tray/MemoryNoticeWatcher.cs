using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vacuo.Core.Notices;
using Vacuo.Core.Ram;
using Vacuo.Core.Settings;
using static Vacuo.Tray.NativeMethods;

namespace Vacuo.Tray;

/// <summary>
/// The tray's memory check (ADR-004): one cheap Windows call a minute. When memory becomes tight and
/// Otomatik Bakım is on, it starts the helper to show the notice; the tray draws nothing itself and closes
/// nothing (vacuo-kurallar 4). Paging is not read here (it needs a second sample over time); the commit
/// charge and the RAM left are enough to see "tight".
/// </summary>
internal sealed class MemoryNoticeWatcher(string helperPath)
{
    private const string MemorySwitch = "--memory";

    private bool _wasTight;
    private DateTimeOffset? _lastNotice;

    public void Check()
    {
        if (Read() is not { } snapshot)
        {
            return;
        }

        MemoryPressure pressure = MemoryPressureRule.Of(snapshot);
        DateTimeOffset now = DateTimeOffset.Now;
        bool tell = MemoryNoticePolicy.ShouldNotify(pressure, _wasTight, _lastNotice, now);
        _wasTight = pressure == MemoryPressure.Tight;
        if (!tell || !File.Exists(helperPath) || !new SettingsStore(SettingsStore.DefaultPath).Load().Maintenance.Enabled)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(helperPath, MemorySwitch) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            _lastNotice = now;
        }
        catch (Win32Exception)
        {
            // Blocked or missing: told next time memory becomes tight.
        }
    }

    private static MemorySnapshot? Read()
    {
        var info = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (!GetPerformanceInfo(ref info, info.Size) || info.CommitLimit == 0 || info.PhysicalTotal == 0)
        {
            return null;
        }

        long page = (long)info.PageSize;
        return new MemorySnapshot(
            TotalPhysicalBytes: (long)info.PhysicalTotal * page,
            AvailablePhysicalBytes: (long)info.PhysicalAvailable * page,
            CommittedBytes: (long)info.CommitTotal * page,
            CommitLimitBytes: (long)info.CommitLimit * page,
            PageReadsPerSecond: 0);
    }
}
