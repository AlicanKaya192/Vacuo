namespace Vacuo.Core.Battery;

/// <summary>
/// Which batteries the Battery card is about: only one that powers the computer, as in a laptop. A desktop
/// has none, and a UPS on a desktop is left out too (it reports itself as short-term), so desktop users
/// never see the card (Alican, 21 October 2026).
/// </summary>
public static class BatteryKind
{
    private const uint SystemBattery = 0x80000000;
    private const uint ShortTerm = 0x20000000;
    private const byte NoSystemBattery = 128;
    private const byte UnknownStatus = 255;

    /// <param name="capabilities">BATTERY_INFORMATION.Capabilities from the battery's driver.</param>
    public static bool IsLaptopBattery(uint capabilities) =>
        (capabilities & SystemBattery) != 0 && (capabilities & ShortTerm) == 0;

    /// <param name="batteryFlag">SYSTEM_POWER_STATUS.BatteryFlag from GetSystemPowerStatus.</param>
    public static bool WindowsReportsBattery(byte batteryFlag) => batteryFlag is not (NoSystemBattery or UnknownStatus);
}
