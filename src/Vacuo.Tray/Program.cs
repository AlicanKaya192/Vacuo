using Vacuo.Core.Hosting;
using Vacuo.Core.Notices;
using Vacuo.Core.Settings;

namespace Vacuo.Tray;

/// <summary>
/// Vacuo's resident process: only the logo in the notification area. A click starts the
/// WinUI panel (Vacuo.exe) as a separate process, or shows it again while that process is
/// still waiting after being closed. Switches: <c>--panel &lt;path&gt;</c>, <c>--lang en-US</c>, <c>--open</c> (show the panel at once),
/// and for Stage 0 measurement <c>--measure-dir &lt;folder&gt;</c>, <c>--linger-seconds &lt;n&gt;</c>
/// with <c>--script idle|coldstart|full|linger</c>.
/// </summary>
internal static class Program
{
    private const string PanelExecutable = "Vacuo.exe";
    private const string MaintenanceExecutable = "Vacuo.Maintenance.exe";
    private const string TrayIconFile = "Assets\\vacuo.ico";
    private const string Tooltip = "Vacuo";
    private const string SingleTrayName = @"Local\Vacuo.Tray";

    [STAThread]
    private static int Main(string[] args)
    {
        // One tray per user: a second start (the Start menu shortcut, a setup) opens the running tray's panel.
        using var singleTray = new Mutex(initiallyOwned: true, SingleTrayName, out bool firstTray);
        if (!firstTray)
        {
            TrayWindow.OpenRunningPanel();
            return 0;
        }

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

        TrayTexts texts = TrayTexts.For(options.PanelArguments, new SettingsStore(SettingsStore.DefaultPath).Load().Language);
        panel.LaunchFailed += () => window.ShowWarning(texts.PanelFailedTitle, texts.PanelFailed);
        window.IconClicked += panel.Toggle;
        window.OpenPanelRequested += panel.Open;
        window.OpenDiskRequested += panel.OpenOnDisk;
        window.OpenOrganizeRequested += panel.OpenOnOrganize;
        window.ClosePanelRequested += panel.Close;
        window.PanelHidden += panel.HandleHidden;
        window.ForegroundRequested += asker =>
            panel.SendData(asker, PanelProtocol.ForegroundDataTag, foreground.Snapshot().Encode());
        // While an update downloads behind a hidden panel, the icon's tooltip shows how far it is.
        window.DataReceived += (sender, tag, data) =>
        {
            if (tag == PanelProtocol.TooltipDataTag && panel.IsPanelWindow(sender) && TrayTooltip.Decode(data) is { } tooltip)
            {
                window.SetTooltip(tooltip);
            }
        };
        window.PanelExited += () =>
        {
            window.SetTooltip(string.Empty);
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

        // Otomatik Bakım: the tray only decides when; the helper does the work and shows the notices (ADR-004).
        string helperPath = Path.Combine(AppContext.BaseDirectory, MaintenanceExecutable);
        var maintenance = new MaintenanceScheduler(helperPath);
        var memory = new MemoryNoticeWatcher(helperPath);
        var disk = new DiskSpaceWatcher(helperPath);
        var organize = new OrganizeScheduleWatcher(helperPath);
        window.TimerTicked += memory.Check;
        window.TimerTicked += disk.Check;
        window.TimerTicked += organize.Check;
        window.TimerTicked += maintenance.Check;
        window.StartTimer(MemoryNoticePolicy.CheckInterval);

        // After setup or from the Start menu the panel shows too, so a new user sees where Vacuo went;
        // starting with Windows passes no switch and stays in the tray.
        if (options.OpenPanelAtStart)
        {
            window.PostOpenPanel();
        }

        if (options.Script is not null && measurement is not null)
        {
            _ = Task.Run(() => new TrayScript(window, measurement).RunAsync(options.Script));
        }

        TrayWindow.RunMessageLoop();
        measurement?.Save(panel.LaunchesMs);
        return 0;
    }
}

internal sealed record TrayOptions(
    string? PanelPath, string? MeasurementDirectory, string? Script, IReadOnlyList<string> PanelArguments, bool OpenPanelAtStart)
{
    public static TrayOptions Parse(IReadOnlyList<string> args)
    {
        string? panelPath = null;
        string? measurementDirectory = null;
        string? script = null;
        bool openPanelAtStart = false;
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
                case "--open":
                    openPanelAtStart = true;
                    break;
                case "--lang" or PanelProtocol.LingerSecondsSwitch when i + 1 < args.Count:
                    // Passed on: the panel shows the text and decides how long it waits.
                    panelArguments.Add(args[i]);
                    panelArguments.Add(args[++i]);
                    break;
            }
        }

        return new TrayOptions(panelPath, measurementDirectory, script, panelArguments, openPanelAtStart);
    }
}
