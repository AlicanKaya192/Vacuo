using Vacuo.Core.Tests.Scanning;
using Vacuo.Core.Updates;

namespace Vacuo.Core.Tests.Updates;

public class UpdateStateStoreTests
{
    [Fact]
    public void NoFile_MeansNeverChecked()
    {
        using var tree = new TempTree();

        Assert.Null(new UpdateStateStore(tree.PathOf(@"Vacuo\update.json")).LoadLastCheck());
    }

    [Fact]
    public void SavedCheck_ComesBackTheSame()
    {
        using var tree = new TempTree();
        var store = new UpdateStateStore(tree.PathOf(@"Vacuo\update.json"));
        var checkedAt = new DateTimeOffset(2026, 10, 20, 22, 15, 30, TimeSpan.Zero);

        store.SaveLastCheck(checkedAt);

        Assert.Equal(checkedAt, store.LoadLastCheck());
    }

    [Fact]
    public void DamagedFile_MeansNeverChecked()
    {
        using var tree = new TempTree();
        string path = tree.PathOf("update.json");
        File.WriteAllText(path, "{ not json");

        Assert.Null(new UpdateStateStore(path).LoadLastCheck());
    }
}
