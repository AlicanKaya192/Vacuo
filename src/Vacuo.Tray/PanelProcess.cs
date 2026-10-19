using System.Diagnostics;
using Vacuo.Core.Hosting;
using static Vacuo.Tray.NativeMethods;

namespace Vacuo.Tray;

/// <summary>
/// Starts the WinUI panel as its own process and closes it again. The panel process ends
/// when the panel hides, so WinUI only uses memory while the panel is on screen.
/// </summary>
internal sealed class PanelProcess(string panelPath, IReadOnlyList<string> panelArguments, Action onExited)
{
    // A tray click first takes focus from the open panel, which then closes itself;
    // the click that follows must not open it again at once.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(250);

    private readonly List<double> _launchesMs = [];
    private Process? _process;
    private DateTime _exitedAt = DateTime.MinValue;

    public bool IsOpen => _process is { HasExited: false };

    public bool QuitAllRequested { get; private set; }

    /// <summary>Milliseconds since the tray started at which each panel process was launched.</summary>
    public IReadOnlyList<double> LaunchesMs => _launchesMs;

    public Func<int, string?> MeasurementPathForLaunch { get; init; } = _ => null;

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        if (DateTime.UtcNow - _exitedAt < ReopenGuard)
        {
            return;
        }

        Open();
    }

    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        var start = new ProcessStartInfo(panelPath) { UseShellExecute = false };
        start.ArgumentList.Add(PanelProtocol.PanelOnlySwitch);
        foreach (string argument in panelArguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (MeasurementPathForLaunch(_launchesMs.Count) is { } measurementPath)
        {
            start.ArgumentList.Add("--measure");
            start.ArgumentList.Add(measurementPath);
        }

        // The tray process received the click, so it may hand the foreground on to the panel.
        AllowSetForegroundWindow(AllowAnyProcess);
        _launchesMs.Add((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds);
        _process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{panelPath}'.");
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => onExited();
    }

    /// <summary>Asks the panel to close the way Alt+F4 would; it then ends its own process.</summary>
    public void Close()
    {
        if (_process is not { HasExited: false } process)
        {
            return;
        }

        foreach (nint window in VisibleWindowsOf(process.Id))
        {
            PostMessage(window, WmClose, 0, 0);
        }
    }

    /// <summary>Call on the tray thread after <c>onExited</c> fired.</summary>
    public void HandleExited()
    {
        if (_process is null)
        {
            return;
        }

        _exitedAt = DateTime.UtcNow;
        QuitAllRequested = _process.ExitCode == PanelProtocol.QuitAllExitCode;
        _process.Dispose();
        _process = null;
    }

    private static List<nint> VisibleWindowsOf(int processId)
    {
        var windows = new List<nint>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == processId && IsWindowVisible(window))
            {
                windows.Add(window);
            }

            return true;
        }, 0);
        return windows;
    }
}
