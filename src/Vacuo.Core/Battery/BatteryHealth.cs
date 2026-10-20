namespace Vacuo.Core.Battery;

/// <summary>
/// One battery as its driver reports it. A value the driver does not report is null. Capacities are
/// in mWh unless <paramref name="RelativeUnits"/>: then the driver uses its own units, still
/// comparable with each other. Rate is in mW, negative while discharging.
/// </summary>
public sealed record BatteryReading(
    long? DesignCapacity,
    long? FullChargeCapacity,
    long? RemainingCapacity,
    int? CycleCount,
    int? RateMilliwatts,
    bool OnAcPower,
    bool Charging,
    bool RelativeUnits);

public enum BatteryBand
{
    Unknown,
    Good,
    Fair,
    Weak,
}

/// <summary>Health as full charge over design capacity, in percent; null when not reported.</summary>
public sealed record BatteryHealth(double? Percent, BatteryBand Band, bool CappedAt100);

/// <summary>Bands agreed in the plan (Aşama 3b): Good from 80 %, Fair from 60 %, Weak below.</summary>
public static class BatteryHealthRule
{
    public const double GoodPercent = 80;
    public const double FairPercent = 60;

    public static BatteryHealth Of(long? designCapacity, long? fullChargeCapacity)
    {
        if (designCapacity is not > 0 || fullChargeCapacity is not { } fullCharge)
        {
            return new BatteryHealth(null, BatteryBand.Unknown, CappedAt100: false);
        }

        // A new battery may hold a little more than its design; above 100 % is shown as 100 % with a note.
        double percent = 100.0 * fullCharge / designCapacity.Value;
        bool capped = percent > 100;
        percent = Math.Min(percent, 100);
        BatteryBand band = percent >= GoodPercent ? BatteryBand.Good
            : percent >= FairPercent ? BatteryBand.Fair
            : BatteryBand.Weak;
        return new BatteryHealth(percent, band, capped);
    }
}
