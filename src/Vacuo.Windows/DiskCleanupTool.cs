using System.Diagnostics;
using Vacuo.Core.Scanning;

namespace Vacuo.Windows;

/// <summary>Opens Windows' own Disk Cleanup, which removes Windows.old; Vacuo does not take ownership of it.</summary>
public static class DiskCleanupTool
{
    public static void Open(WindowsLocations locations)
    {
        string cleanmgr = Path.Combine(locations.Windows, "System32", "cleanmgr.exe");
        using Process? _ = Process.Start(new ProcessStartInfo(cleanmgr) { UseShellExecute = true });
    }
}
