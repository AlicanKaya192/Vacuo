using Microsoft.UI.Xaml;
using Vacuo.App.Localization;
using Vacuo.App.Measurement;
using Vacuo.App.Tray;
using Vacuo.Core.Hosting;

namespace Vacuo.App;

public partial class App : Application
{
    private const string TrayIconFile = "Assets\\vacuo.ico";

    private PanelWindow? _panel;
    private TrayIcon? _tray;
    private MeasurementLog? _measurement;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var options = LaunchOptions.Parse(Environment.GetCommandLineArgs());
        var localizer = new Localizer(options.Language);
        _measurement = options.MeasurementPath is null ? null : new MeasurementLog(options.MeasurementPath);

        _panel = new PanelWindow(localizer, _measurement, options.ForceReducedMotion);

        if (options.PanelOnly)
        {
            RunAsPanelProcess(_panel);
            return;
        }

        _panel.QuitRequested += (_, _) => Quit(PanelProtocol.PanelClosedExitCode);
        string iconPath = Path.Combine(AppContext.BaseDirectory, TrayIconFile);
        _tray = new TrayIcon(_panel.Handle, iconPath, localizer.Get("TrayTooltip"));
        _tray.Clicked += (_, _) => _panel.Toggle();

        if (options.OpenPanelAtStart)
        {
            _panel.Toggle();
        }

        if (options.Script is not null && _measurement is not null)
        {
            _ = new MeasurementScript(_panel, _measurement, () => Quit(PanelProtocol.PanelClosedExitCode))
                .RunAsync(options.Script);
        }
    }

    // Started by Vacuo.Tray: show the panel at once and end the process when it hides,
    // so the WinUI memory is only in use while the panel is on screen.
    private void RunAsPanelProcess(PanelWindow panel)
    {
        // Queued: Hidden can fire inside the window's own Closing handler.
        panel.Hidden += (_, _) => panel.DispatcherQueue.TryEnqueue(() => Quit(PanelProtocol.PanelClosedExitCode));
        panel.QuitRequested += (_, _) => Quit(PanelProtocol.QuitAllExitCode);
        panel.Toggle();
    }

    private void Quit(int exitCode)
    {
        _tray?.Dispose();
        _measurement?.Save();
        _panel?.Shutdown();
        Environment.ExitCode = exitCode;
        Exit();
    }
}
