using Vacuo.Core.Battery;

namespace Vacuo.Windows.Tests;

/// <summary>Reads this machine's batteries through their drivers; only reads, changes nothing.</summary>
public class BatteryReadingTests
{
    [Fact]
    public void Readings_AreConsistent()
    {
        // A desktop has none; every battery found must have sane numbers.
        foreach (BatteryReading battery in BatteryReader.Read())
        {
            Assert.True(battery.DesignCapacity is null or > 0);
            Assert.True(battery.FullChargeCapacity is null or > 0);
            Assert.True(battery.RemainingCapacity is null || battery.RemainingCapacity <= battery.FullChargeCapacity * 11 / 10);
            Assert.True(battery.CycleCount is null or > 0);
        }
    }

    [Fact(Explicit = true)]
    public void PrintThisMachine()
    {
        IReadOnlyList<BatteryReading> batteries = BatteryReader.Read();
        TestContext.Current.TestOutputHelper!.WriteLine($"{batteries.Count} battery");
        foreach (BatteryReading battery in batteries)
        {
            BatteryHealth health = BatteryHealthRule.Of(battery.DesignCapacity, battery.FullChargeCapacity);
            TestContext.Current.TestOutputHelper!.WriteLine(
                $"design {battery.DesignCapacity}, full {battery.FullChargeCapacity}, remaining {battery.RemainingCapacity} " +
                $"({(battery.RelativeUnits ? "relative" : "mWh")}); cycles {battery.CycleCount?.ToString() ?? "not reported"}; " +
                $"rate {battery.RateMilliwatts} mW; AC {battery.OnAcPower}, charging {battery.Charging}; " +
                $"health {health.Percent:0.0} % {health.Band}{(health.CappedAt100 ? " (capped)" : "")}");
        }
    }
}
