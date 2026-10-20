using System.Diagnostics;
using Vacuo.Core.Duplicates;

namespace Vacuo.Windows.Tests;

/// <summary>Looks for copies in this machine's default folders; only reads, and prints no file names.</summary>
public class DuplicateReadingTests
{
    [Fact(Explicit = true)]
    public void PrintThisMachine()
    {
        var clock = Stopwatch.StartNew();
        DuplicateScan scan = DuplicateFinder.Find(DuplicateFinder.DefaultRoots(), DuplicateFinder.DefaultMinimumBytes, CancellationToken.None);

        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;
        output.WriteLine(
            $"{scan.Groups.Count} groups, {scan.Groups.Sum(group => group.Files.Count)} files, " +
            $"wasted {scan.Groups.Sum(group => group.WastedBytes) / 1e9:0.00} GB in {clock.ElapsedMilliseconds} ms; skipped {scan.SkippedEntries}");
        foreach (DuplicateGroup group in scan.Groups.Take(5))
        {
            output.WriteLine($"  {group.Files.Count} x {group.FileBytes / 1e6:0.0} MB ({Path.GetExtension(group.Files[0].Path)})");
        }
    }
}
