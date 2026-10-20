using Vacuo.Core.Ram;

namespace Vacuo.Windows.Tests;

/// <summary>Reads this machine's real counters and process list; only reads, never closes anything.</summary>
public class MemoryReadingTests
{
    [Fact]
    public void Counters_AreConsistent()
    {
        MemorySnapshot snapshot = MemoryCounters.Read(TimeSpan.FromMilliseconds(200));

        Assert.InRange(snapshot.AvailablePhysicalBytes, 1, snapshot.TotalPhysicalBytes);
        Assert.InRange(snapshot.CommittedBytes, 1, snapshot.CommitLimitBytes);
        Assert.True(snapshot.PageReadsPerSecond >= 0);
    }

    [Fact]
    public void RunningApps_ProtectTheShellAndListThisTestRunner()
    {
        IReadOnlyList<RunningApp> apps = RunningApps.List();

        Assert.Contains(apps, app => app.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase) && app.IsProtected);
        Assert.Contains(apps, app => app.Name.Equals("svchost", StringComparison.OrdinalIgnoreCase) && app.IsProtected);
        Assert.All(apps, app => Assert.True(app.PrivateBytes >= 0));
    }

    [Fact(Explicit = true)]
    public void PrintThisMachine()
    {
        MemorySnapshot snapshot = MemoryCounters.Read(TimeSpan.FromSeconds(1));
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"pressure {MemoryPressureRule.Of(snapshot)}; commit {snapshot.CommittedBytes >> 20} / {snapshot.CommitLimitBytes >> 20} MB; " +
            $"available {snapshot.AvailablePhysicalBytes >> 20} / {snapshot.TotalPhysicalBytes >> 20} MB; page reads {snapshot.PageReadsPerSecond:0.0}/s");
        for (int run = 0; run < 3; run++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int count = RunningApps.List().Count;
            TestContext.Current.TestOutputHelper!.WriteLine($"RunningApps.List: {count} apps in {clock.ElapsedMilliseconds} ms");
        }

        foreach (RunningApp app in RunningApps.List().OrderByDescending(app => app.PrivateBytes).Take(15))
        {
            TestContext.Current.TestOutputHelper!.WriteLine(
                $"{app.Name}: {app.PrivateBytes >> 20} MB, window {app.HasWindow}, protected {app.IsProtected}");
        }
    }
}
