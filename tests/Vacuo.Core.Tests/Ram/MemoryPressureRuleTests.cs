using Vacuo.Core.Ram;

namespace Vacuo.Core.Tests.Ram;

public class MemoryPressureRuleTests
{
    private const long Gb = 1L << 30;

    // 32 GB of RAM and a 34 GB commit limit, like this machine.
    private static MemorySnapshot Snapshot(double commitRatio, double availableRatio, double pageReadsPerSecond = 0) => new(
        TotalPhysicalBytes: 32 * Gb,
        AvailablePhysicalBytes: (long)Math.Ceiling(32 * Gb * availableRatio),
        CommittedBytes: (long)Math.Ceiling(34 * Gb * commitRatio),
        CommitLimitBytes: 34 * Gb,
        PageReadsPerSecond: pageReadsPerSecond);

    [Fact]
    public void PlentyLeft_IsComfortable()
    {
        Assert.Equal(MemoryPressure.Comfortable, MemoryPressureRule.Of(Snapshot(0.62, 0.45)));
    }

    [Fact]
    public void ManyUsedPagesButFreeCommit_IsStillComfortable()
    {
        // Windows fills free RAM with cache; little "available" alone is not pressure above 15 %.
        Assert.Equal(MemoryPressure.Comfortable, MemoryPressureRule.Of(Snapshot(0.50, 0.16)));
    }

    [Theory]
    [InlineData(0.75, 0.40)]
    [InlineData(0.50, 0.14)]
    public void CommitNearingTheLimitOrLittleRamLeft_IsFilling(double commit, double available)
    {
        Assert.Equal(MemoryPressure.Filling, MemoryPressureRule.Of(Snapshot(commit, available)));
    }

    [Theory]
    [InlineData(0.90, 0.40, 0)]
    [InlineData(0.50, 0.04, 0)]
    [InlineData(0.50, 0.10, 50)]
    public void CommitAtTheLimitOrRamGoneOrHeavyPaging_IsTight(double commit, double available, double pageReads)
    {
        Assert.Equal(MemoryPressure.Tight, MemoryPressureRule.Of(Snapshot(commit, available, pageReads)));
    }

    [Fact]
    public void HeavyPagingWithRamLeft_IsNotTight()
    {
        // Paging also happens when a program starts and reads its files; it only counts when RAM is short.
        Assert.Equal(MemoryPressure.Comfortable, MemoryPressureRule.Of(Snapshot(0.50, 0.40, 500)));
    }

    [Fact]
    public void NoCommitLimit_Throws()
    {
        Assert.Throws<ArgumentException>(() => MemoryPressureRule.Of(Snapshot(0.5, 0.5) with { CommitLimitBytes = 0 }));
    }
}
