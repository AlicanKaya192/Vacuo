using System.ComponentModel;
using System.Diagnostics;
using Vacuo.Core.Uninstall;

namespace Vacuo.Windows;

public enum UninstallerExit
{
    Closed,
    Cancelled,
    NotFound,
}

/// <summary>
/// Starts a program's own uninstaller in its own window, exactly as Windows Settings would, and waits
/// for it to close. Vacuo adds no switches and asks for no rights itself; if the uninstaller needs an
/// administrator, Windows asks the user for the uninstaller.
/// </summary>
public static class UninstallerLauncher
{
    private const int CancelledByUser = 1223;
    private const int FileNotFound = 2;
    private const int PathNotFound = 3;

    public static async Task<UninstallerExit> RunAsync(UninstallCommand command)
    {
        var start = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(command.Program), command.Arguments)
        {
            UseShellExecute = true,
        };

        try
        {
            using Process? uninstaller = Process.Start(start);
            if (uninstaller is not null)
            {
                await uninstaller.WaitForExitAsync();
            }

            return UninstallerExit.Closed;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == CancelledByUser)
        {
            return UninstallerExit.Cancelled;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is FileNotFound or PathNotFound)
        {
            return UninstallerExit.NotFound;
        }
    }
}
