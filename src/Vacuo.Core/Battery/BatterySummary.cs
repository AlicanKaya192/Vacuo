namespace Vacuo.Core.Battery;

public enum BatteryState
{
    Unknown,
    Discharging,
    Charging,
    PluggedIn,
}

/// <summary>What the Battery card and screen show: every system battery added up.</summary>
public sealed record BatterySummary(
    long? DesignCapacity,
    long? FullChargeCapacity,
    double? ChargePercent,
    BatteryHealth Health,
    int? CycleCount,
    int? RateMilliwatts,
    BatteryState State,
    TimeSpan? TimeEstimate,
    bool RelativeUnits)
{
    public static BatterySummary? From(IReadOnlyList<BatteryReading> batteries)
    {
        if (batteries.Count == 0)
        {
            return null;
        }

        long? design = Sum(batteries, battery => battery.DesignCapacity);
        long? fullCharge = Sum(batteries, battery => battery.FullChargeCapacity);
        long? remaining = Sum(batteries, battery => battery.RemainingCapacity);
        long? rate = Sum(batteries, battery => battery.RateMilliwatts);
        bool relative = batteries.Any(battery => battery.RelativeUnits);
        BatteryState state = StateOf(batteries, remaining, rate);

        return new BatterySummary(
            design,
            fullCharge,
            fullCharge > 0 && remaining is { } left ? Math.Min(100.0, 100.0 * left / fullCharge.Value) : null,
            BatteryHealthRule.Of(design, fullCharge),
            // Cycles belong to one battery; a sum over two would mean nothing.
            batteries.Count == 1 ? batteries[0].CycleCount : null,
            (int?)rate,
            state,
            relative ? null : Estimate(state, fullCharge, remaining, rate),
            relative);
    }

    private static BatteryState StateOf(IReadOnlyList<BatteryReading> batteries, long? remaining, long? rate)
    {
        if (batteries.Any(battery => battery.Charging))
        {
            return BatteryState.Charging;
        }

        if (batteries.Any(battery => battery.OnAcPower))
        {
            return BatteryState.PluggedIn;
        }

        return remaining is null && rate is null ? BatteryState.Unknown : BatteryState.Discharging;
    }

    // At the present rate only: the time left while discharging, the time to full while charging.
    private static TimeSpan? Estimate(BatteryState state, long? fullCharge, long? remaining, long? rate) => (state, rate) switch
    {
        (BatteryState.Discharging, < 0) when remaining is { } left => TimeSpan.FromHours((double)left / -rate.Value),
        (BatteryState.Charging, > 0) when remaining is { } left && fullCharge is { } full =>
            TimeSpan.FromHours((double)Math.Max(0, full - left) / rate.Value),
        _ => null,
    };

    // Unknown as soon as one battery does not report it.
    private static long? Sum(IReadOnlyList<BatteryReading> batteries, Func<BatteryReading, long?> value)
    {
        long total = 0;
        foreach (BatteryReading battery in batteries)
        {
            if (value(battery) is not { } part)
            {
                return null;
            }

            total += part;
        }

        return total;
    }
}
