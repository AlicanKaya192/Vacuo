namespace Vacuo.Core.Updates;

/// <summary>
/// How the panel starts a downloaded setup (installer/vacuo.iss): no window, no message boxes, no restart, no
/// "ready to install" page, and the panel's and the tray's process ids, so setup waits until both have closed.
/// </summary>
public static class InstallerCommand
{
    private const string Silent = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";

    public static string Arguments(int panelProcessId, int? trayProcessId) => trayProcessId is { } tray
        ? $"{Silent} /OLDPID={panelProcessId} /OLDTRAYPID={tray}"
        : $"{Silent} /OLDPID={panelProcessId}";
}
