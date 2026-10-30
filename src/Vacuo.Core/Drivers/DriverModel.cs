namespace Vacuo.Core.Drivers;

/// <summary>The device classes the Drivers screen lists, in the order it lists them.</summary>
public enum DriverClass
{
    Display,
    Network,
    Sound,
}

/// <summary>One installed driver as Windows records it; anything may be missing but the name.</summary>
public sealed record DriverEntry(DriverClass Class, string Name, string? Provider, string? Version, DateOnly? Date);

/// <summary>What the Drivers screen lists, and how many of Windows' own drivers it leaves out.</summary>
public sealed record DriverOverview(IReadOnlyList<DriverEntry> Shown, int FromWindows)
{
    public static DriverOverview From(IEnumerable<DriverEntry> entries)
    {
        int fromWindows = 0;
        var shown = new List<DriverEntry>();
        foreach (DriverEntry entry in entries)
        {
            if (DriverAgeRule.IsFromWindows(entry.Provider))
            {
                fromWindows++;
            }
            else if (!shown.Contains(entry))
            {
                shown.Add(entry);
            }
        }

        shown.Sort((a, b) => a.Class != b.Class
            ? a.Class.CompareTo(b.Class)
            : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return new DriverOverview(shown, fromWindows);
    }
}

/// <summary>The BIOS and the machine it belongs to. A serial number is deliberately not part of it.</summary>
public sealed record BiosInfo(string? Version, DateOnly? Date, string? Vendor, string? Manufacturer, string? Model)
{
    // What firmware leaves in these fields when the maker did not fill them in, on a self-built PC above all.
    private static readonly string[] Placeholders =
    [
        "System manufacturer",
        "System Product Name",
        "To be filled by O.E.M.",
        "Default string",
        "Not Specified",
    ];

    public static BiosInfo From(
        string? version,
        string? releaseDate,
        string? vendor,
        string? systemManufacturer,
        string? boardManufacturer,
        string? systemProduct,
        string? boardProduct) => new(
            Named(version),
            DriverDates.Read(releaseDate),
            Named(vendor),
            Named(systemManufacturer) ?? Named(boardManufacturer),
            Named(systemProduct) ?? Named(boardProduct));

    private static string? Named(string? text)
    {
        string? trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) || Placeholders.Contains(trimmed, StringComparer.OrdinalIgnoreCase) ? null : trimmed;
    }
}
