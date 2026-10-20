namespace Vacuo.Core.Hosting;

/// <summary>
/// How the small tray process and the WinUI panel process talk. The tray starts the panel
/// with <see cref="PanelOnlySwitch"/> and its own window handle after <see cref="TrayWindowSwitch"/>.
/// A hidden panel waits <see cref="LingerAfterHide"/> before its process ends, so a quick
/// reopen is instant; meanwhile the two exchange the registered window messages below.
/// The panel exits with <see cref="QuitAllExitCode"/> when the user quits Vacuo altogether.
/// </summary>
public static class PanelProtocol
{
    public const string PanelOnlySwitch = "--panel-only";
    public const string TrayWindowSwitch = "--tray-window";
    /// <summary>Measurement only: a shorter wait than <see cref="LingerAfterHide"/>.</summary>
    public const string LingerSecondsSwitch = "--linger-seconds";

    /// <summary>Tray to panel: show the panel again.</summary>
    public const string ShowPanelMessage = "Vacuo.ShowPanel";
    /// <summary>Tray to panel: end the panel process now.</summary>
    public const string QuitPanelMessage = "Vacuo.QuitPanel";
    /// <summary>Panel to tray: the panel was hidden.</summary>
    public const string PanelHiddenMessage = "Vacuo.PanelHidden";
    /// <summary>
    /// Panel to tray, with the panel window in wParam: send the foreground history. The tray answers
    /// only a window of the panel process it started, with WM_COPYDATA tagged <see cref="ForegroundDataTag"/>.
    /// </summary>
    public const string ForegroundRequestMessage = "Vacuo.ForegroundRequest";
    /// <summary>"VACF": marks the WM_COPYDATA that carries a <see cref="Ram.ForegroundSnapshot"/>.</summary>
    public const int ForegroundDataTag = 0x5641_4346;

    public const int PanelClosedExitCode = 0;
    public const int QuitAllExitCode = 2;

    public static readonly TimeSpan LingerAfterHide = TimeSpan.FromMinutes(5);
}
