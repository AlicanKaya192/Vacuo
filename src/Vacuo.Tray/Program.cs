using Vacuo.Core.Hosting;

namespace Vacuo.Tray;

/// <summary>
/// Vacuo's resident process: only the logo in the notification area. A click starts the
/// WinUI panel (Vacuo.exe) as a separate process, or shows it again while that process is
/// still waiting after being closed. Switches: <c>--panel &lt;path&gt;</c>, <c>--lang en-US</c>,
/// and for Stage 0 measurement <c>--measure-dir &lt;folder&gt;</c>, <c>--linger-seconds &lt;n&gt;</c>
/// with <c>--script idle|coldstart|full|linger</c>.
/// </summary>
internal static class Program
{
    private const string PanelExecutable = "Vacuo.exe";
    private const string TrayIconFile = "Assets\\vacuo.ico";
    private const string Tooltip = "Vacuo";

    [STAThread]
    private static int Main(string[] args)
    {
        var options = TrayOptions.Parse(args);
        var measurement = options.MeasurementDirectory is null ? null : new TrayMeasurement(options.MeasurementDirectory);

        using var window = new TrayWindow(Path.Combine(AppContext.BaseDirectory, TrayIconFile), Tooltip);
        using var foreground = new ForegroundWatcher();
        var panel = new PanelProcess(
            options.PanelPath ?? Path.Combine(AppContext.BaseDirectory, PanelExecutable),
            window.Handle,
            options.PanelArguments,
            window.PostPanelExited)
        {
            MeasurementPathForLaunch = launch => measurement?.PanelFile(launch),
        };

        window.IconClicked += panel.Toggle;
        window.OpenPanelRequested += panel.Open;
        window.ClosePanelRequested += panel.Close;
        window.PanelHidden += panel.HandleHidden;
        window.ForegroundRequested += asker =>
            panel.SendData(asker, PanelProtocol.ForegroundDataTag, foreground.Snapshot().Encode());
        window.PanelExited += () =>
        {
            panel.HandleExited();
            if (panel.QuitAllRequested)
            {
                window.Quit();
            }
        };
        window.QuitRequested += () =>
        {
            panel.Quit();
            window.Quit();
        };

        if (options.Script is not null && measurement is not null)
        {
            _ = Task.Run(() => new TrayScript(window, measurement).RunAsync(options.Script));
        }

        TrayWindow.RunMessageLoop();
        measurement?.Save(panel.LaunchesMs);
        return 0;
    }
}

internal sealed record TrayOptions(string? PanelPath, string? MeasurementDirectory, string? Script, IReadOnlyList<string> PanelArguments)
{
    public static TrayOptions Parse(IReadOnlyList<string> args)
    {
        string? panelPath = null;
        string? measurementDirectory = null;
        string? script = null;
        var panelArguments = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--panel" when i + 1 < args.Count:
                    panelPath = args[++i];
                    break;
                case "--measure-dir" when i + 1 < args.Count:
                    measurementDirectory = args[++i];
                    break;
                case "--script" when i + 1 < args.Count:
                    script = args[++i];
                    break;
                case "--lang" or PanelProtocol.LingerSecondsSwitch when i + 1 < args.Count:
                    // Passed on: the panel shows the text and decides how long it waits.
                    panelArguments.Add(args[i]);
                    panelArguments.Add(args[++i]);
                    break;
            }
        }

        return new TrayOptions(panelPath, measurementDirectory, script, panelArguments);
    }
}
