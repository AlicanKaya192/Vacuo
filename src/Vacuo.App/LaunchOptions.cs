using System.Globalization;
using Vacuo.Core.Hosting;

namespace Vacuo.App;

/// <summary>
/// Command-line switches: <c>--panel-only</c> with <c>--tray-window &lt;handle&gt;</c> (started by
/// the tray process), and for Stage 0 measurement <c>--lang en-US</c>, <c>--measure &lt;file.json&gt;</c>,
/// <c>--open</c>, <c>--script idle|coldstart|full</c>, <c>--reduced-motion</c>, <c>--linger-seconds &lt;n&gt;</c>.
/// </summary>
internal sealed record LaunchOptions(
    string? Language,
    string? MeasurementPath,
    bool OpenPanelAtStart,
    string? Script,
    bool ForceReducedMotion,
    bool PanelOnly,
    nint TrayWindow,
    TimeSpan Linger)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? language = null;
        string? measurementPath = null;
        bool openPanel = false;
        string? script = null;
        bool reducedMotion = false;
        bool panelOnly = false;
        nint trayWindow = 0;
        TimeSpan linger = PanelProtocol.LingerAfterHide;

        for (int i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--lang" when i + 1 < args.Count:
                    language = args[++i];
                    break;
                case "--measure" when i + 1 < args.Count:
                    measurementPath = args[++i];
                    break;
                case "--open":
                    openPanel = true;
                    break;
                case "--script" when i + 1 < args.Count:
                    script = args[++i];
                    break;
                case "--reduced-motion":
                    reducedMotion = true;
                    break;
                case PanelProtocol.PanelOnlySwitch:
                    panelOnly = true;
                    break;
                case PanelProtocol.TrayWindowSwitch when i + 1 < args.Count:
                    trayWindow = (nint)long.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case PanelProtocol.LingerSecondsSwitch when i + 1 < args.Count:
                    linger = TimeSpan.FromSeconds(int.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
            }
        }

        return new LaunchOptions(language, measurementPath, openPanel, script, reducedMotion, panelOnly, trayWindow, linger);
    }
}
