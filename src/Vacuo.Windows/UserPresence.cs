using System.Runtime.InteropServices;

namespace Vacuo.Windows;

/// <summary>
/// Whether the user is at the computer and whether it runs on mains power, for Otomatik Bakım (ADR-004).
/// Two cheap Windows calls; nothing is watched or recorded.
/// </summary>
public static class UserPresence
{
    private const byte AcOnline = 1;

    /// <summary>How long since the last keyboard or mouse input in this session.</summary>
    public static TimeSpan IdleFor()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        // Both are milliseconds since start-up and wrap together after 49 days.
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    public static DateTimeOffset LastInputAt() => DateTimeOffset.Now - IdleFor();

    /// <summary>Plugged in; a computer without a battery reports itself on mains. Unknown counts as not.</summary>
    public static bool OnMains() => GetSystemPowerStatus(out SystemPowerStatus status) && status.ACLineStatus == AcOnline;

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
