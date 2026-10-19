namespace Vacuo.Core.Hosting;

/// <summary>
/// How the small tray process and the WinUI panel process talk: the tray starts the panel
/// with <see cref="PanelOnlySwitch"/>; the panel exits when it hides, and exits with
/// <see cref="QuitAllExitCode"/> when the user chose to quit Vacuo altogether.
/// </summary>
public static class PanelProtocol
{
    public const string PanelOnlySwitch = "--panel-only";
    public const int PanelClosedExitCode = 0;
    public const int QuitAllExitCode = 2;
}
