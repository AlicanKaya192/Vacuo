using Microsoft.UI.Xaml;
using Vacuo.App.Hosting;
using Vacuo.App.Localization;
using Vacuo.App.Measurement;
using Vacuo.App.Tray;
using Vacuo.Core.Hosting;

namespace Vacuo.App;

public partial class App : Application
{
    internal const string IconFile = "Assets\\vacuo.ico";
    private static readonly TimeSpan ForegroundTimeout = TimeSpan.FromSeconds(1);

    private PanelWindow? _panel;
    private TrayIcon? _tray;
    private TrayChannel? _channel;
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
            RunAsPanelProcess(_panel, options);
            return;
        }

        _panel.QuitRequested += (_, _) => Quit(PanelProtocol.PanelClosedExitCode);
        string iconPath = Path.Combine(AppContext.BaseDirectory, IconFile);
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

    // Started by Vacuo.Tray: show the panel at once. Once hidden, the process waits a while
    // so a quick reopen is instant, then ends so the WinUI memory is given back.
    private void RunAsPanelProcess(PanelWindow panel, LaunchOptions options)
    {
        _channel = new TrayChannel(panel.Handle, options.TrayWindow);
        panel.ForegroundSource = () => _channel.RequestForegroundAsync(ForegroundTimeout);
        var linger = panel.DispatcherQueue.CreateTimer();
        linger.Interval = options.Linger;
        linger.IsRepeating = false;
        linger.Tick += (_, _) => Quit(PanelProtocol.PanelClosedExitCode);

        panel.Hidden += (_, _) =>
        {
            _channel.NotifyPanelHidden();
            linger.Start();
        };
        _channel.ShowRequested += (_, _) =>
        {
            linger.Stop();
            panel.Reveal();
        };
        _channel.QuitRequested += (_, _) => Quit(PanelProtocol.PanelClosedExitCode);
        panel.QuitRequested += (_, _) => Quit(PanelProtocol.QuitAllExitCode);
        panel.Toggle();
    }

    private void Quit(int exitCode)
    {
        _channel?.Dispose();
        _tray?.Dispose();
        _measurement?.Save();
        _panel?.Shutdown();
        Environment.ExitCode = exitCode;
        Exit();
    }
}
