using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Vacuo.Core.Ram;
using static Vacuo.Tray.NativeMethods;

namespace Vacuo.Tray;

/// <summary>
/// Notes which program comes to the front, for the Memory screen's "unused for" (B-135, B-137).
/// Windows calls back on the tray thread through its message loop; nothing polls, and the history
/// stays in this process's memory only.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private const int MaxPath = 1024;

    // Kept in a field so the delegate is not collected while Windows still calls it.
    private readonly WinEventProc _callback;
    private readonly nint _hook;
    private readonly ForegroundHistory _history;

    public ForegroundWatcher()
    {
        _history = new ForegroundHistory(DateTimeOffset.UtcNow);
        _callback = OnForegroundChanged;
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, _callback, 0, 0, WinEventOutOfContext);
        if (_hook == 0)
        {
            throw new Win32Exception("Could not watch which program is in front.");
        }

        Note(GetForegroundWindow());
    }

    public ForegroundSnapshot Snapshot() => _history.Snapshot();

    public void Dispose() => UnhookWinEvent(_hook);

    private void OnForegroundChanged(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time) =>
        Note(window);

    private void Note(nint window)
    {
        if (window != 0 && ProgramNameOf(window) is { } name)
        {
            _history.Switched(name, DateTimeOffset.UtcNow);
        }
    }

    // The same name Process.ProcessName gives, so the panel can match it to the running apps.
    private static string? ProgramNameOf(nint window)
    {
        GetWindowThreadProcessId(window, out uint processId);
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var path = new StringBuilder(MaxPath);
            int size = path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref size) ? Path.GetFileNameWithoutExtension(path.ToString()) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
