using System.Globalization;
using System.Text.RegularExpressions;

namespace Vacuo.Core.Drivers;

/// <summary>
/// Which drivers look old: more than a year past their date. That is only a hint, the maker's latest may be
/// that old too. Windows' own drivers are left out, their dates can be far older than the driver.
/// </summary>
public static class DriverAgeRule
{
    public static bool IsFromWindows(string? provider) =>
        provider?.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) == true;

    public static bool LooksOld(DriverEntry driver, DateOnly today) =>
        !IsFromWindows(driver.Provider) && driver.Date is { } date && date < today.AddYears(-1);
}

/// <summary>Reads the dates Windows writes in the driver keys (7-23-2026) and the BIOS key (07/23/2026).</summary>
public static class DriverDates
{
    private static readonly string[] Formats = ["M-d-yyyy", "M/d/yyyy"];

    public static DateOnly? Read(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
}

/// <summary>
/// The official support pages of the makers Vacuo knows, checked by hand on 30 October 2026. The address opens in
/// the browser; Vacuo itself never goes to them. Addresses carry no region: the sites choose one themselves.
/// </summary>
public static class SupportPages
{
    private static readonly (Regex Maker, Uri Page)[] Known =
    [
        (Maker("asus|asustek"), new("https://www.asus.com/support/")),
        (Maker("lenovo"), new("https://support.lenovo.com/")),
        (Maker("hp|hewlett[- ]packard"), new("https://support.hp.com/")),
        (Maker("dell"), new("https://www.dell.com/support/")),
        (Maker("acer"), new("https://www.acer.com/support")),
        (Maker("msi|micro-star"), new("https://www.msi.com/support")),
        (Maker("gigabyte"), new("https://www.gigabyte.com/Support")),
        (Maker("asrock"), new("https://www.asrock.com/support/index.asp")),
        (Maker("nvidia"), new("https://www.nvidia.com/en-us/drivers/")),
        (Maker("amd|advanced micro devices"), new("https://www.amd.com/en/support/download/drivers.html")),
        (Maker("intel"), new("https://www.intel.com/content/www/us/en/download-center/home.html")),
        (Maker("realtek"), new("https://www.realtek.com/Download/Index")),
    ];

    /// <summary>Where a maker Vacuo does not know is sent instead.</summary>
    public static Uri WindowsUpdate { get; } = new("ms-settings:windowsupdate");

    public static Uri? For(string? vendor) =>
        string.IsNullOrEmpty(vendor) ? null : Array.Find(Known, known => known.Maker.IsMatch(vendor)).Page;

    // Whole words only: "HP" must not match inside another name, nor "Intel" inside "Intelligent".
    private static Regex Maker(string names) =>
        new($@"\b(?:{names})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
