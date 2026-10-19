using System.ComponentModel;
using System.Diagnostics;
using Vacuo.Core.Leftovers;

namespace Vacuo.Windows;

/// <summary>
/// Starts Vacuo.Elevated through the Windows administrator prompt (ADR-003), waits for it and
/// reads what it did. Vacuo itself never runs as administrator; the helper gets operation ids
/// and a request id, never a path.
/// </summary>
public sealed class ElevatedLauncher(string helperPath, string resultFolder) : IElevatedLauncher
{
    private const string HelperFile = "Vacuo.Elevated.exe";
    // The user picked "No" at the administrator prompt.
    private const int ErrorCancelled = 1223;
    private const int HelperRefused = 2;

    public static ElevatedLauncher NextToThisApp() =>
        new(Path.Combine(AppContext.BaseDirectory, HelperFile), ElevatedResults.DefaultFolder);

    public IReadOnlyList<ElevatedResult> Run(IReadOnlyList<string> operations)
    {
        var requestId = Guid.NewGuid();
        var start = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            // Ids and a GUID only: none of them has spaces or quotes.
            Arguments = string.Join(' ', [.. operations, requestId.ToString("D")]),
        };

        int exitCode;
        try
        {
            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException($"'{helperPath}' did not start.");
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            throw new ElevationDeclinedException();
        }

        if (exitCode == HelperRefused)
        {
            throw new InvalidOperationException("The administrator helper refused the request; nothing was touched.");
        }

        return ElevatedResults.Read(resultFolder, requestId);
    }
}
