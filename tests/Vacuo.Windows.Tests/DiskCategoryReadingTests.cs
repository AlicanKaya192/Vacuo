using System.Diagnostics;
using Vacuo.Core.Disk;
using Vacuo.Core.Scanning;

namespace Vacuo.Windows.Tests;

/// <summary>Measures this machine's known folders by category; only reads.</summary>
public class DiskCategoryReadingTests
{
    [Fact(Explicit = true)]
    public void PrintThisMachine()
    {
        var clock = Stopwatch.StartNew();
        IReadOnlyList<CategorySize> sizes = CategorySizer.Measure(CategoryRoots.ForThisMachine());
        long measuredMs = clock.ElapsedMilliseconds;

        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        long used = drive.TotalSize - drive.AvailableFreeSpace;
        DiskCategories categories = DiskCategories.From(sizes, ScanSummary.Combine([]), used);

        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;
        output.WriteLine($"{sizes.Count} roots measured in {measuredMs} ms; skipped folders {sizes.Sum(size => size.Size.SkippedEntries)}");
        foreach (CategoryTotal total in categories.Totals)
        {
            output.WriteLine($"{total.Category}: {total.Bytes / 1e9:0.0} GB ({total.Fraction:P0})");
        }

        foreach (CategorySize size in sizes.OrderByDescending(size => size.Size.Bytes).Take(6))
        {
            output.WriteLine($"  {size.Root.Category} {Path.GetFileName(size.Root.Path)}: {size.Size.Bytes / 1e9:0.0} GB");
        }
    }
}
