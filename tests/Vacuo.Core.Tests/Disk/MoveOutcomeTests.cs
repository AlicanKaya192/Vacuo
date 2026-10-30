using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;

namespace Vacuo.Core.Tests.Disk;

public class MoveOutcomeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static JournalEntry Moved(string name, long bytes) =>
        new(Guid.NewGuid(), Now, bytes, new RecycledItem(Path.Join(@"C:\Users\test", name), Now, @"C:\$Recycle.Bin\x"), null);

    private static MoveOutcome OutcomeOf(params JournalEntry[] moved) => new(moved, []);

    [Fact]
    public void Largest_NothingMoved_IsNull() => Assert.Null(OutcomeOf().Largest);

    // With one item the total already says it all.
    [Fact]
    public void Largest_OneItem_IsNull() => Assert.Null(OutcomeOf(Moved("a", 300)).Largest);

    [Fact]
    public void Largest_ManyItems_IsTheBiggestWithItsShare()
    {
        LargestMove? largest = OutcomeOf(Moved("small", 250), Moved("big", 750)).Largest;

        Assert.Equal(new LargestMove(@"C:\Users\test\big", 750, 75), largest);
    }

    // Folders Windows could not size count as nothing: there is no share to tell.
    [Fact]
    public void Largest_NoSizesKnown_IsNull() => Assert.Null(OutcomeOf(Moved("a", 0), Moved("b", 0)).Largest);

    // "100%" would say it was the only thing that moved.
    [Fact]
    public void Largest_NearlyEverything_DoesNotRoundUpToAll()
    {
        Assert.Equal(99, OutcomeOf(Moved("big", 9_996), Moved("small", 4)).Largest!.Percent);
    }

    [Fact]
    public void Largest_ManyEqualItems_StillShowsAShare()
    {
        JournalEntry[] many = Enumerable.Range(0, 300).Select(i => Moved($"item{i}", 10)).ToArray();

        Assert.Equal(1, OutcomeOf(many).Largest!.Percent);
    }
}
