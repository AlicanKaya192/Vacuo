using Vacuo.Core.Battery;

namespace Vacuo.Core.Tests.Battery;

public class BatteryHealthRuleTests
{
    private const long Design = 50_000;

    [Fact]
    public void FullChargeNearTheDesign_IsGood()
    {
        BatteryHealth health = BatteryHealthRule.Of(Design, 45_000);

        Assert.Equal(90, health.Percent);
        Assert.Equal(BatteryBand.Good, health.Band);
        Assert.False(health.CappedAt100);
    }

    [Theory]
    [InlineData(40_000, BatteryBand.Good)]
    [InlineData(39_950, BatteryBand.Fair)]
    [InlineData(30_000, BatteryBand.Fair)]
    [InlineData(29_950, BatteryBand.Weak)]
    [InlineData(0, BatteryBand.Weak)]
    public void BandsChangeAt80And60Percent(long fullCharge, BatteryBand band)
    {
        Assert.Equal(band, BatteryHealthRule.Of(Design, fullCharge).Band);
    }

    [Fact]
    public void MoreThanTheDesign_CountsAs100AndSaysSo()
    {
        BatteryHealth health = BatteryHealthRule.Of(Design, 52_000);

        Assert.Equal(100, health.Percent);
        Assert.Equal(BatteryBand.Good, health.Band);
        Assert.True(health.CappedAt100);
    }

    [Theory]
    [InlineData(null, 45_000L)]
    [InlineData(Design, null)]
    [InlineData(0L, 45_000L)]
    public void NotReported_IsUnknown(long? design, long? fullCharge)
    {
        BatteryHealth health = BatteryHealthRule.Of(design, fullCharge);

        Assert.Null(health.Percent);
        Assert.Equal(BatteryBand.Unknown, health.Band);
    }
}
