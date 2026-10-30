using System.Security;
using Microsoft.Win32;
using Vacuo.Core.Drivers;

namespace Vacuo.Windows;

/// <summary>
/// Reads the BIOS key and the driver classes Windows keeps in the registry. Opens every key read-only and needs no
/// administrator; never writes. The BIOS key is read by value name, so a serial number never comes along.
/// </summary>
public static class DeviceDriverReader
{
    private const string BiosPath = @"HARDWARE\DESCRIPTION\System\BIOS";
    private const string ClassPath = @"SYSTEM\CurrentControlSet\Control\Class\";

    private static readonly (DriverClass Class, string Guid)[] Classes =
    [
        (DriverClass.Display, "{4d36e968-e325-11ce-bfc1-08002be10318}"),
        (DriverClass.Network, "{4d36e972-e325-11ce-bfc1-08002be10318}"),
        (DriverClass.Sound, "{4d36e96c-e325-11ce-bfc1-08002be10318}"),
    ];

    public static BiosInfo? ReadBios()
    {
        using RegistryKey? bios = Registry.LocalMachine.OpenSubKey(BiosPath, writable: false);
        return bios is null
            ? null
            : BiosInfo.From(
                Text(bios, "BIOSVersion"),
                Text(bios, "BIOSReleaseDate"),
                Text(bios, "BIOSVendor"),
                Text(bios, "SystemManufacturer"),
                Text(bios, "BaseBoardManufacturer"),
                Text(bios, "SystemProductName"),
                Text(bios, "BaseBoardProduct"));
    }

    public static IReadOnlyList<DriverEntry> ReadDrivers()
    {
        var drivers = new List<DriverEntry>();
        foreach ((DriverClass kind, string guid) in Classes)
        {
            using RegistryKey? devices = Registry.LocalMachine.OpenSubKey(ClassPath + guid, writable: false);
            foreach (string name in devices?.GetSubKeyNames() ?? [])
            {
                if (IsDeviceKey(name) && Read(devices!, name, kind) is { } driver)
                {
                    drivers.Add(driver);
                }
            }
        }

        return drivers;
    }

    // Devices are numbered 0000, 0001, ...; the class's other subkeys hold no driver and some of them deny reading.
    private static bool IsDeviceKey(string name) => name.Length == 4 && name.All(char.IsAsciiDigit);

    private static DriverEntry? Read(RegistryKey devices, string name, DriverClass kind)
    {
        try
        {
            using RegistryKey? device = devices.OpenSubKey(name, writable: false);
            return Text(device, "DriverDesc") is { Length: > 0 } description
                ? new DriverEntry(
                    kind,
                    description,
                    Text(device, "ProviderName"),
                    Text(device, "DriverVersion"),
                    DriverDates.Read(Text(device, "DriverDate")))
                : null;
        }
        catch (SecurityException)
        {
            return null;
        }
    }

    private static string? Text(RegistryKey? key, string name) => key?.GetValue(name) as string;
}
