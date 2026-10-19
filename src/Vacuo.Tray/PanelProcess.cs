using System.Diagnostics;
using System.Globalization;
using Vacuo.Core.Hosting;
using static Vacuo.Tray.NativeMethods;

namespace Vacuo.Tray;

/// <summary>
/// Starts the WinUI panel as its own process, shows a hidden one again, and closes it.
/// A hidden panel process keeps running for <see cref="PanelProtocol.LingerAfterHide"/>
/// and then ends on its own, so WinUI only holds memory around the time it is used.
/// </summary>
internal sealed class PanelProcess(string panelPath, nint trayWindow, IReadOnlyList<string> panelArguments, Action onExited)
{
    // A tray click first takes focus from the open panel, which then hides itself;
    // the click that follows must not show it again at once.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(250);

    private readonly uint _showMessage = RegisterWindowMessage(PanelProtocol.ShowPanelMessage);
    private readonly uint _quitMessage = RegisterWindowMessage(PanelProtocol.QuitPanelMessage);
    private readonly List<double> _launchesMs = [];
    private Process? _process;
    private bool _panelVisible;
    private DateTime _hiddenAt = DateTime.MinValue;

    public bool QuitAllRequested { get; private set; }

    /// <summary>Milliseconds since the tray started at which each panel process was launched.</summary>
    public IReadOnlyList<double> LaunchesMs => _launchesMs;

    public Func<int, string?> MeasurementPathForLaunch { get; init; } = _ => null;

    private bool IsRunning => _process is { HasExited: false };

    public void Toggle()
    {
        if (IsRunning && _panelVisible)
        {
            Close();
            return;
        }

        if (DateTime.UtcNow - _hiddenAt < ReopenGuard)
        {
            return;
        }

        Open();
    }

    public void Open()
    {
        if (IsRunning && _panelVisible)
        {
            return;
        }

        // The tray process received the click, so it may hand the foreground on to the panel.
        AllowSetForegroundWindow(AllowAnyProcess);
        _panelVisible = true;
        if (IsRunning)
        {
            PostToPanel(_showMessage);
            return;
        }

        Launch();
    }

    /// <summary>Asks the panel to close the way Alt+F4 would; it hides and starts waiting.</summary>
    public void Close()
    {
        if (_process is not { HasExited: false } process)
        {
            return;
        }

        // Only the visible panel window: WM_CLOSE would destroy WinUI's hidden helper windows.
        foreach (nint window in TopLevelWindowsOf(process.Id).Where(IsWindowVisible))
        {
            PostMessage(window, WmClose, 0, 0);
        }
    }

    /// <summary>Ends a waiting or open panel process, for example when the tray quits.</summary>
    public void Quit()
    {
        if (IsRunning)
        {
            PostToPanel(_quitMessage);
        }
    }

    /// <summary>Call on the tray thread when the panel reported that it was hidden.</summary>
    public void HandleHidden()
    {
        _panelVisible = false;
        _hiddenAt = DateTime.UtcNow;
    }

    /// <summary>Call on the tray thread after <c>onExited</c> fired.</summary>
    public void HandleExited()
    {
        if (_process is null)
        {
            return;
        }

        HandleHidden();
        QuitAllRequested = _process.ExitCode == PanelProtocol.QuitAllExitCode;
        _process.Dispose();
        _process = null;
    }

    private void Launch()
    {
        var start = new ProcessStartInfo(panelPath) { UseShellExecute = false };
        start.ArgumentList.Add(PanelProtocol.PanelOnlySwitch);
        start.ArgumentList.Add(PanelProtocol.TrayWindowSwitch);
        start.ArgumentList.Add(((long)trayWindow).ToString(CultureInfo.InvariantCulture));
        foreach (string argument in panelArguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (MeasurementPathForLaunch(_launchesMs.Count) is { } measurementPath)
        {
            start.ArgumentList.Add("--measure");
            start.ArgumentList.Add(measurementPath);
        }

        _launchesMs.Add((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds);
        _process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{panelPath}'.");
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => onExited();
    }

    // For the registered Vacuo messages only. The panel listens on its main window; WinUI's
    // hidden helper windows pass these unknown messages to their default handling.
    private void PostToPanel(uint message)
    {
        if (_process is null)
        {
            return;
        }

        foreach (nint window in TopLevelWindowsOf(_process.Id))
        {
            PostMessage(window, message, 0, 0);
        }
    }

    private static List<nint> TopLevelWindowsOf(int processId)
    {
        var windows = new List<nint>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == processId)
            {
                windows.Add(window);
            }

            return true;
        }, 0);
        return windows;
    }
}
