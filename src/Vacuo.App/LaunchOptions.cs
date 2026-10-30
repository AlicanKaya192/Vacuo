using System.Globalization;
using Vacuo.Core.Hosting;

namespace Vacuo.App;

/// <summary>
/// Command-line switches: <c>--panel-only</c> with <c>--tray-window &lt;handle&gt;</c> (started by
/// the tray process, and <c>--show-disk</c> or <c>--show-organize</c> to open on that screen), and for Stage 0 measurement <c>--lang en-US</c>, <c>--measure &lt;file.json&gt;</c>,
/// <c>--open</c>, <c>--script idle|coldstart|full|memory|memory-cards</c>, <c>--reduced-motion</c>, <c>--linger-seconds &lt;n&gt;</c>,
/// and <c>--probe no-void|no-ring|no-3d|narrow</c> (repeatable, only with <c>--measure</c>) to find what the panel's memory comes from.
/// </summary>
internal sealed record LaunchOptions(
    string? Language,
    string? MeasurementPath,
    bool OpenPanelAtStart,
    string? Script,
    bool ForceReducedMotion,
    bool PanelOnly,
    bool StartOnDisk,
    bool StartOnOrganize,
    nint TrayWindow,
    TimeSpan Linger,
    IReadOnlySet<string> Probes)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? language = null;
        string? measurementPath = null;
        bool openPanel = false;
        string? script = null;
        bool reducedMotion = false;
        bool panelOnly = false;
        bool startOnDisk = false;
        bool startOnOrganize = false;
        nint trayWindow = 0;
        TimeSpan linger = PanelProtocol.LingerAfterHide;
        var probes = new HashSet<string>(StringComparer.Ordinal);

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
                case "--probe" when i + 1 < args.Count:
                    probes.Add(args[++i]);
                    break;
                case PanelProtocol.PanelOnlySwitch:
                    panelOnly = true;
                    break;
                case PanelProtocol.ShowDiskSwitch:
                    startOnDisk = true;
                    break;
                case PanelProtocol.ShowOrganizeSwitch:
                    startOnOrganize = true;
                    break;
                case PanelProtocol.TrayWindowSwitch when i + 1 < args.Count:
                    trayWindow = (nint)long.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case PanelProtocol.LingerSecondsSwitch when i + 1 < args.Count:
                    linger = TimeSpan.FromSeconds(int.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
            }
        }

        return new LaunchOptions(language, measurementPath, openPanel, script, reducedMotion, panelOnly, startOnDisk, startOnOrganize, trayWindow, linger, probes);
    }
}
