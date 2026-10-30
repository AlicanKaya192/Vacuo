using System.Globalization;

namespace Vacuo.Core.Formatting;

/// <summary>
/// Formats byte counts the way Windows Explorer does: binary units (1 KB = 1024 B)
/// with one decimal, in the given culture's number format.
/// </summary>
public static class ByteSizeFormatter
{
    private const double BytesPerUnitStep = 1024;
    private static readonly string[] UnitsAboveBytes = ["KB", "MB", "GB", "TB"];

    public static string Format(long bytes, CultureInfo culture)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        if (bytes < BytesPerUnitStep)
        {
            return string.Create(culture, $"{bytes} B");
        }

        double value = bytes;
        int unitIndex = -1;
        while (value >= BytesPerUnitStep && unitIndex < UnitsAboveBytes.Length - 1)
        {
            value /= BytesPerUnitStep;
            unitIndex++;
        }

        return string.Create(culture, $"{value:0.0} {UnitsAboveBytes[unitIndex]}");
    }

    /// <summary>A size that went up or down: "+2,0 GB", "−2,0 GB"; no change has no sign.</summary>
    public static string FormatChange(long bytes, CultureInfo culture)
    {
        string size = Format(Math.Abs(bytes), culture);
        return bytes switch
        {
            > 0 => "+" + size,
            < 0 => "−" + size,
            _ => size,
        };
    }
}
