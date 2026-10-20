using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Battery;

namespace Vacuo.App.Screens;

/// <summary>What the Battery card and screen say; the texts live in the .resw files.</summary>
internal static class BatteryTexts
{
    /// <summary>"%93 · Good", or only the band when the health is not known.</summary>
    public static string Health(Localizer localizer, BatteryHealth health) => health.Percent is { } percent
        ? localizer.Format("BatteryHealthFormat", percent, BandName(localizer, health.Band))
        : BandName(localizer, health.Band);

    public static string BandName(Localizer localizer, BatteryBand band) => localizer.Get($"BatteryBand_{band}");

    public static string BandHint(Localizer localizer, BatteryBand band) => localizer.Get($"BatteryBandHint_{band}");

    // Calm is the accent colour; only a weak battery warns.
    public static Brush BandBrush(BatteryBand band) => (Brush)Application.Current.Resources[band switch
    {
        BatteryBand.Weak => "WarningBrush",
        BatteryBand.Unknown => "InkSecondaryBrush",
        _ => "AccentBrush",
    }];

    /// <summary>"Charge 89% · on battery · about 2 h 3 min left".</summary>
    public static string CardLine(Localizer localizer, BatterySummary summary)
    {
        string state = localizer.Get($"BatteryState_{summary.State}");
        string line = summary.ChargePercent is { } charge ? localizer.Format("BatteryCardChargeFormat", charge, state) : state;
        return Estimate(localizer, summary) is { } estimate ? $"{line} · {estimate}" : line;
    }

    public static string? Estimate(Localizer localizer, BatterySummary summary) => summary.TimeEstimate is not { } time
        ? null
        : localizer.Format(
            summary.State == BatteryState.Charging ? "BatteryTimeToFullFormat" : "BatteryTimeLeftFormat",
            MemoryTexts.Duration(localizer, time));

    public static string Energy(Localizer localizer, long? capacity, bool relativeUnits) => capacity switch
    {
        null => localizer.Get("BatteryNotKnown"),
        { } units when relativeUnits => localizer.Format("BatteryRelativeFormat", units),
        { } milliwattHours => localizer.Format("BatteryEnergyFormat", milliwattHours / 1000.0),
    };

    public static string Percent(Localizer localizer, double? percent) =>
        percent is { } value ? localizer.Format("BatteryPercentFormat", value) : localizer.Get("BatteryNotKnown");

    public static string Power(Localizer localizer, int? milliwatts) =>
        milliwatts is { } value ? localizer.Format("BatteryPowerFormat", Math.Abs(value) / 1000.0) : localizer.Get("BatteryNotKnown");
}
