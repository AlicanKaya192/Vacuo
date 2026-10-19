using Vacuo.Core.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Actions;

public class MoveToRecycleBinActionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Execute_HandsThePathToTheRecycleBinAndReturnsItsRecord()
    {
        using var tree = new TempTree();
        string cache = tree.AddFile(@"Local\pip\Cache\entry", 30);
        var bin = new FakeRecycleBin(tree.PathOf("bin"), new FixedTimeProvider(Now));

        UndoRecord undo = new MoveToRecycleBinAction(tree.PathOf(@"Local\pip\Cache"), bin).Execute();

        Assert.False(File.Exists(cache));
        Assert.Equal(1, bin.ItemCount);
        Assert.Equal(new RecycledItem(tree.PathOf(@"Local\pip\Cache"), Now), undo);
    }

    [Fact]
    public void Execute_MissingPath_RefusesWithoutCallingTheRecycleBin()
    {
        using var tree = new TempTree();
        var bin = new FakeRecycleBin(tree.PathOf("bin"), new FixedTimeProvider(Now));

        Assert.Throws<ActionRefusedException>(() => new MoveToRecycleBinAction(tree.PathOf("gone"), bin).Execute());

        Assert.Equal(0, bin.ItemCount);
    }
}
