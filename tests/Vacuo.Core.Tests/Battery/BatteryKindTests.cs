using Vacuo.Core.Battery;

namespace Vacuo.Core.Tests.Battery;

public class BatteryKindTests
{
    // BATTERY_SYSTEM_BATTERY, BATTERY_CAPACITY_RELATIVE and BATTERY_IS_SHORT_TERM (Batclass.h).
    private const uint SystemBattery = 0x80000000;
    private const uint CapacityRelative = 0x40000000;
    private const uint ShortTerm = 0x20000000;

    [Theory]
    [InlineData(SystemBattery, true)]
    [InlineData(SystemBattery | CapacityRelative, true)]
    [InlineData(0u, false)]
    public void ALaptopBattery_CountsOnlyWhenItPowersTheSystem(uint capabilities, bool counts)
    {
        Assert.Equal(counts, BatteryKind.IsLaptopBattery(capabilities));
    }

    // A desktop's UPS reports itself as a short-term system battery; its owner has no laptop battery to look after.
    [Fact]
    public void AnUps_DoesNotCount()
    {
        Assert.False(BatteryKind.IsLaptopBattery(SystemBattery | ShortTerm));
    }

    // GetSystemPowerStatus BatteryFlag: 128 no system battery, 255 unknown; anything else describes one.
    [Theory]
    [InlineData(128, false)]
    [InlineData(255, false)]
    [InlineData(1, true)]
    [InlineData(8, true)]
    public void Windows_SaysWhetherThereIsABattery(byte batteryFlag, bool hasBattery)
    {
        Assert.Equal(hasBattery, BatteryKind.WindowsReportsBattery(batteryFlag));
    }
}
