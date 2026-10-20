using Vacuo.Core.Maintenance;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Maintenance;

public class MaintenanceStateStoreTests
{
    [Fact]
    public void NoFile_MeansNeverRun()
    {
        using var tree = new TempTree();
        var store = new MaintenanceStateStore(tree.PathOf(@"Vacuo\maintenance.json"));

        Assert.Null(store.LoadLastRun());
        Assert.Null(store.LoadLastSummary());
    }

    [Fact]
    public void SavedRun_ComesBackTheSame()
    {
        using var tree = new TempTree();
        var store = new MaintenanceStateStore(tree.PathOf(@"Vacuo\maintenance.json"));
        var at = new DateTimeOffset(2026, 10, 21, 3, 5, 0, TimeSpan.FromHours(3));
        var summary = new MaintenanceSummary(Moved: 4, Bytes: 1_500_000, Failed: 1, Stopped: true) { Organized = 2 };

        store.SaveRun(at, summary);

        Assert.Equal(at, store.LoadLastRun());
        Assert.Equal(summary, store.LoadLastSummary());
    }

    [Fact]
    public void DamagedFile_MeansNeverRun()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("maintenance.json");
        File.WriteAllText(path, "{ not json");

        Assert.Null(new MaintenanceStateStore(path).LoadLastRun());
    }
}
