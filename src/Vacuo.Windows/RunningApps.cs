using System.Diagnostics;
using Vacuo.Core.Ram;

namespace Vacuo.Windows;

/// <summary>
/// The programs running now, one entry per program name with its processes' private memory
/// summed. Processes in another session (services) and the Windows shell itself are protected.
/// Only reads; nothing is opened for writing or closed here.
/// </summary>
public static class RunningApps
{
    // Parts of Windows that run in the user's session, and Vacuo itself.
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "conhost", "dllhost", "taskhostw", "audiodg",
        "RuntimeBroker", "ApplicationFrameHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "TextInputHost", "LockApp", "SecurityHealthSystray", "Vacuo", "Vacuo.Tray", "Vacuo.Elevated",
    };

    public static IReadOnlyList<RunningApp> List()
    {
        using Process self = Process.GetCurrentProcess();
        int session = self.SessionId;
        HashSet<int> withWindow = TopLevelWindows.Switchable().Select(window => window.ProcessId).ToHashSet();
        Process[] processes = Process.GetProcesses();
        try
        {
            return processes
                .GroupBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new RunningApp(
                    group.Key,
                    group.Sum(PrivateBytes),
                    group.Any(process => withWindow.Contains(process.Id)),
                    ProtectedNames.Contains(group.Key) || group.Any(process => process.SessionId != session)))
                .ToList();
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    // A process can end between listing and reading; it then counts as nothing.
    private static long PrivateBytes(Process process)
    {
        try
        {
            return process.PrivateMemorySize64;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    /// <summary>Parts of Windows and Vacuo itself: listed, never asked to close.</summary>
    public static bool IsProtectedName(string name) => ProtectedNames.Contains(name);
}
