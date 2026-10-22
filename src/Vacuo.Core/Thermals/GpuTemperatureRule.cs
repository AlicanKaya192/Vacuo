namespace Vacuo.Core.Thermals;

/// <summary>What one display adapter answered: its name and its temperature in tenths of a degree.</summary>
public sealed record AdapterReading(string Name, uint DeciCelsius);

/// <summary>A graphics card's temperature in degrees Celsius.</summary>
public sealed record GpuTemperature(string Name, double Celsius);

/// <summary>
/// Turns what Windows' display kernel reports (D3DKMT adapter performance data, the source Task Manager uses)
/// into a temperature worth showing. Only the graphics card: a processor's temperature needs a kernel driver
/// on Windows, and Vacuo installs none (vacuo-kurallar 5; Alican, 22 October 2026: "ekran kartını yapalım sadece").
/// </summary>
public static class GpuTemperatureRule
{
    // An adapter without a sensor answers 0; anything this hot is a misread, not a temperature.
    private const uint NotReported = 0;
    private const uint ImplausiblyHot = 1500;

    public static double? FromDeciCelsius(uint deciCelsius) =>
        deciCelsius is NotReported or >= ImplausiblyHot ? null : deciCelsius / 10.0;

    /// <summary>The warmest adapter that reports a temperature; null when none does.</summary>
    public static GpuTemperature? Hottest(IEnumerable<AdapterReading> adapters) =>
        adapters
            .Select(adapter => (adapter.Name, Celsius: FromDeciCelsius(adapter.DeciCelsius)))
            .Where(reading => reading.Celsius is not null)
            .Select(reading => new GpuTemperature(reading.Name, reading.Celsius!.Value))
            .MaxBy(reading => reading.Celsius);
}
