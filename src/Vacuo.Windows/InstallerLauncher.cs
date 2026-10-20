using System.ComponentModel;
using System.Diagnostics;

namespace Vacuo.Windows;

public enum InstallerStart
{
    Started,

    /// <summary>Windows refused to run it; for an unsigned setup usually Smart App Control.</summary>
    Blocked,
    Failed,
}

/// <summary>
/// Starts a verified setup (<see cref="Core.Updates.InstallerCommand"/> gives the arguments). Windows refusing
/// the program is told apart from other failures so the screen can say why: 4551 application control policy
/// (Smart App Control; Odyssey measured it on its unsigned setup), 1260 group policy or AppLocker, 225 and
/// 226 antivirus.
/// </summary>
public static class InstallerLauncher
{
    private static readonly int[] BlockedErrors = [4551, 1260, 225, 226];

    public static InstallerStart Start(string setupPath, string arguments)
    {
        try
        {
            using Process? setup = Process.Start(new ProcessStartInfo(setupPath, arguments) { UseShellExecute = false });
            return setup is null ? InstallerStart.Failed : InstallerStart.Started;
        }
        catch (Win32Exception exception)
        {
            return Classify(exception.NativeErrorCode);
        }
    }

    internal static InstallerStart Classify(int nativeError) =>
        BlockedErrors.Contains(nativeError) ? InstallerStart.Blocked : InstallerStart.Failed;
}
