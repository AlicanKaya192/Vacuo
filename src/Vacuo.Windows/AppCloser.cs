using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Vacuo.Windows;

/// <summary>
/// Asks a program to close the way Alt+F4 would: WM_CLOSE to each visible window of its processes
/// in this session. The program decides; one with unsaved work asks "save changes?" first. Nothing is
/// ever terminated (vacuo-kurallar 4), and protected programs are refused.
/// </summary>
public static class AppCloser
{
    private const uint WmClose = 0x0010;

    /// <summary>Returns how many windows were asked to close; 0 when the program is not running here.</summary>
    public static int RequestClose(string appName)
    {
        if (RunningApps.IsProtectedName(appName))
        {
            throw new InvalidOperationException($"'{appName}' is protected; Vacuo never asks it to close.");
        }

        HashSet<int> processIds = ProcessesInThisSession(appName);
        int asked = 0;
        foreach ((nint window, int processId) in TopLevelWindows.Switchable())
        {
            if (processIds.Contains(processId) && PostMessage(window, WmClose, 0, 0))
            {
                asked++;
            }
        }

        return asked;
    }

    /// <summary>
    /// Brings a window of the program to the front, restored if minimised, so the user can answer
    /// its "save changes?" question (B-155). False when it has no window here.
    /// </summary>
    public static bool BringToFront(string appName)
    {
        HashSet<int> processIds = ProcessesInThisSession(appName);
        foreach ((nint window, int processId) in TopLevelWindows.Switchable())
        {
            if (processIds.Contains(processId))
            {
                if (IsIconic(window))
                {
                    ShowWindow(window, SwRestore);
                }

                return SetForegroundWindow(window);
            }
        }

        return false;
    }

    private const int SwRestore = 9;

    private static HashSet<int> ProcessesInThisSession(string appName)
    {
        using Process self = Process.GetCurrentProcess();
        Process[] processes = Process.GetProcessesByName(appName);
        try
        {
            return processes.Where(process => process.SessionId == self.SessionId).Select(process => process.Id).ToHashSet();
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
}
