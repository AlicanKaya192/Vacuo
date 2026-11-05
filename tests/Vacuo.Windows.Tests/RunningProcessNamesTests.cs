using System.Diagnostics;

namespace Vacuo.Windows.Tests;

/// <summary>Reads this machine's real process list; only reads.</summary>
public class RunningProcessNamesTests
{
    [Fact]
    public void Snapshot_HoldsThisTestRunnerIgnoringCase()
    {
        using Process self = Process.GetCurrentProcess();

        IReadOnlySet<string> names = RunningProcessNames.Snapshot();

        Assert.Contains(self.ProcessName.ToUpperInvariant(), names);
        Assert.Contains("explorer", names);
    }
}
