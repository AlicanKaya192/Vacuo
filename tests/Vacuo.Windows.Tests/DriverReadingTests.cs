using Vacuo.Core.Drivers;

namespace Vacuo.Windows.Tests;

/// <summary>Reads this machine's BIOS and driver keys; only reads, changes nothing.</summary>
public class DriverReadingTests
{
    [Fact]
    public void EveryDriver_HasAName()
    {
        Assert.All(DeviceDriverReader.ReadDrivers(), driver => Assert.False(string.IsNullOrWhiteSpace(driver.Name)));
    }

    [Fact]
    public void Bios_IsRead()
    {
        // Every machine this runs on has the key; a value inside it may still be missing.
        Assert.NotNull(DeviceDriverReader.ReadBios());
    }

    [Fact(Explicit = true)]
    public void PrintThisMachine()
    {
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;
        foreach (DriverEntry driver in DeviceDriverReader.ReadDrivers())
        {
            output.WriteLine($"{driver.Class} | {driver.Name} | {driver.Provider} | {driver.Version} | {driver.Date}");
        }
    }
}
