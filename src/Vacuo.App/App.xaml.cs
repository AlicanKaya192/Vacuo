using Microsoft.UI.Xaml;
using Vacuo.App.Localization;
using Vacuo.App.Measurement;
using Vacuo.App.Tray;

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

        _panel = new PanelWindow(localizer, _measurement);
        _panel.QuitRequested += (_, _) => Quit();

        string iconPath = Path.Combine(AppContext.BaseDirectory, TrayIconFile);
        _tray = new TrayIcon(_panel.Handle, iconPath, localizer.Get("TrayTooltip"));
        _tray.Clicked += (_, _) => _panel.Toggle();

        if (options.OpenPanelAtStart)
        {
            _panel.Toggle();
        }
    }

    private void Quit()
    {
        _tray?.Dispose();
        _measurement?.Save();
        _panel?.Shutdown();
        Exit();
    }
}
