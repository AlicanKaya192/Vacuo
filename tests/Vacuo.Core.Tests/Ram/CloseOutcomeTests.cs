using Vacuo.Core.Ram;

namespace Vacuo.Core.Tests.Ram;

public class CloseOutcomeTests
{
    private const long Gb = 1L << 30;
    private static readonly MemorySnapshot Before = new(32 * Gb, 10 * Gb, 24 * Gb, 34 * Gb, PageReadsPerSecond: 0);

    private static RunningApp App(string name) => new(name, 500L << 20, HasWindow: true, IsProtected: false);

    [Fact]
    public void AppNoLongerRunning_IsClosed_AndOneStillRunningIsNot()
    {
        CloseOutcome outcome = CloseOutcome.From(["blender", "brave"], [App("brave"), App("code")], Before, Before);

        Assert.Equal(["blender"], outcome.Closed);
        Assert.Equal(["brave"], outcome.StillRunning);
    }

    [Fact]
    public void Names_IgnoreCase()
    {
        CloseOutcome outcome = CloseOutcome.From(["Blender"], [App("blender")], Before, Before);

        Assert.Empty(outcome.Closed);
        Assert.Equal(["Blender"], outcome.StillRunning);
    }

    [Fact]
    public void Difference_IsWhatWasMeasured_EvenWhenItWentTheOtherWay()
    {
        var after = Before with { AvailablePhysicalBytes = 12 * Gb, CommittedBytes = 25 * Gb };

        CloseOutcome outcome = CloseOutcome.From(["blender"], [], Before, after);

        Assert.Equal(2 * Gb, outcome.AvailableGainedBytes);
        Assert.Equal(-1 * Gb, outcome.CommitDroppedBytes);
    }

    [Fact]
    public void NothingRequested_Throws()
    {
        Assert.Throws<ArgumentException>(() => CloseOutcome.From([], [], Before, Before));
    }
}
