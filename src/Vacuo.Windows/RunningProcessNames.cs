using System.Diagnostics;

namespace Vacuo.Windows;

/// <summary>
/// The names of the processes running now, compared without case, such as <c>msedge</c>.
/// Used to tell whether a browser is open; only reads.
/// </summary>
public static class RunningProcessNames
{
    public static IReadOnlySet<string> Snapshot()
    {
        Process[] processes = Process.GetProcesses();
        try
        {
            return processes.Select(process => process.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }
}
