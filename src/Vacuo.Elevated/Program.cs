using System.Diagnostics;
using Vacuo.Core.Leftovers;
using Vacuo.Core.Scanning;

namespace Vacuo.Elevated;

/// <summary>
/// Vacuo's administrator helper (ADR-003). Vacuo starts it with "run as administrator" for one
/// run: <c>Vacuo.Elevated &lt;operation&gt;... &lt;request-id&gt;</c>. It never takes a path.
/// </summary>
internal static class Program
{
    private static int Main(string[] args) => ElevatedApp.Run(
        args,
        WindowsLocations.ForThisMachine(),
        new SystemProcessRunner(),
        ElevatedResults.DefaultFolder);

    private sealed class SystemProcessRunner : IProcessRunner
    {
        public int Run(string fileName, IReadOnlyList<string> arguments)
        {
            var start = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
