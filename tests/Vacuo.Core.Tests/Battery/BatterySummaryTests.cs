using Vacuo.Core.Battery;

namespace Vacuo.Core.Tests.Battery;

public class BatterySummaryTests
{
    private static BatteryReading Reading(
        long? remaining = 45_000, int? rate = -15_000, bool onAc = false, bool charging = false, bool relative = false, int? cycles = 120) =>
        new(DesignCapacity: 60_000, FullChargeCapacity: 50_000, RemainingCapacity: remaining, CycleCount: cycles,
            RateMilliwatts: rate, OnAcPower: onAc, Charging: charging, RelativeUnits: relative);

    [Fact]
    public void NoBattery_HasNoSummary()
    {
        Assert.Null(BatterySummary.From([]));
    }

    [Fact]
    public void OneBattery_GivesChargeHealthAndWear()
    {
        BatterySummary summary = BatterySummary.From([Reading()])!;

        Assert.Equal(90, summary.ChargePercent);
        Assert.Equal(50_000.0 / 60_000 * 100, summary.Health.Percent!.Value, 6);
        Assert.Equal(BatteryBand.Good, summary.Health.Band);
        Assert.Equal(120, summary.CycleCount);
    }

    [Fact]
    public void Discharging_EstimatesTheTimeLeft()
    {
        BatterySummary summary = BatterySummary.From([Reading(remaining: 45_000, rate: -15_000)])!;

        Assert.Equal(BatteryState.Discharging, summary.State);
        Assert.Equal(TimeSpan.FromHours(3), summary.TimeEstimate);
    }

    [Fact]
    public void Charging_EstimatesTheTimeToFull()
    {
        BatterySummary summary = BatterySummary.From([Reading(remaining: 40_000, rate: 20_000, onAc: true, charging: true)])!;

        Assert.Equal(BatteryState.Charging, summary.State);
        Assert.Equal(TimeSpan.FromMinutes(30), summary.TimeEstimate);
    }

    [Fact]
    public void PluggedInAndNotCharging_HasNoEstimate()
    {
        BatterySummary summary = BatterySummary.From([Reading(rate: 0, onAc: true)])!;

        Assert.Equal(BatteryState.PluggedIn, summary.State);
        Assert.Null(summary.TimeEstimate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownRateOrRelativeUnits_HasNoEstimate(bool relative)
    {
        BatteryReading reading = relative ? Reading(relative: true) : Reading(rate: null);

        Assert.Null(BatterySummary.From([reading])!.TimeEstimate);
    }

    [Fact]
    public void ChargeAboveFull_CountsAs100()
    {
        Assert.Equal(100, BatterySummary.From([Reading(remaining: 51_000)])!.ChargePercent);
    }

    [Fact]
    public void TwoBatteries_AreAddedUp()
    {
        BatterySummary summary = BatterySummary.From([Reading(remaining: 45_000), Reading(remaining: 15_000, cycles: 300)])!;

        Assert.Equal(120_000, summary.DesignCapacity);
        Assert.Equal(100_000, summary.FullChargeCapacity);
        Assert.Equal(60, summary.ChargePercent);
        Assert.Equal(-30_000, summary.RateMilliwatts);
        Assert.Equal(TimeSpan.FromHours(2), summary.TimeEstimate);
        // Cycles belong to one battery; a sum would mean nothing.
        Assert.Null(summary.CycleCount);
    }

    [Fact]
    public void OneBatteryWithoutDesign_MakesTheTotalUnknown()
    {
        BatteryReading unknown = Reading() with { DesignCapacity = null };

        BatterySummary summary = BatterySummary.From([Reading(), unknown])!;

        Assert.Null(summary.DesignCapacity);
        Assert.Equal(BatteryBand.Unknown, summary.Health.Band);
    }
}
