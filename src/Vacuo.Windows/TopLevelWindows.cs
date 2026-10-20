using System.Runtime.InteropServices;

namespace Vacuo.Windows;

/// <summary>Visible, titled top-level windows without an owner: the ones a user can switch to and close.</summary>
internal static class TopLevelWindows
{
    private const uint GwOwner = 4;

    public static List<(nint Window, int ProcessId)> Switchable()
    {
        var windows = new List<(nint, int)>();
        EnumWindows((window, parameter) =>
        {
            if (IsWindowVisible(window) && GetWindow(window, GwOwner) == 0 && GetWindowTextLength(window) > 0)
            {
                _ = GetWindowThreadProcessId(window, out uint processId);
                windows.Add((window, (int)processId));
            }

            return true;
        }, 0);
        return windows;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
