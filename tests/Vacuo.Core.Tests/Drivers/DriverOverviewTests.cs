using Vacuo.Core.Drivers;

namespace Vacuo.Core.Tests.Drivers;

public class DriverOverviewTests
{
    private static DriverEntry Driver(DriverClass kind, string name, string? provider = "Intel", string? version = "1.0") =>
        new(kind, name, provider, version, new DateOnly(2026, 1, 1));

    [Fact]
    public void WindowsOwnDrivers_AreCountedAndNotShown()
    {
        DriverOverview overview = DriverOverview.From(
        [
            Driver(DriverClass.Network, "WAN Miniport (IP)", "Microsoft"),
            Driver(DriverClass.Network, "WAN Miniport (SSTP)", "Microsoft"),
            Driver(DriverClass.Network, "Wi-Fi adapter"),
        ]);

        Assert.Equal("Wi-Fi adapter", Assert.Single(overview.Shown).Name);
        Assert.Equal(2, overview.FromWindows);
    }

    [Fact]
    public void TheSameDriverTwice_IsShownOnce()
    {
        DriverOverview overview = DriverOverview.From(
        [
            Driver(DriverClass.Sound, "Speaker"),
            Driver(DriverClass.Sound, "Speaker"),
            Driver(DriverClass.Sound, "Speaker", version: "2.0"),
        ]);

        Assert.Equal(2, overview.Shown.Count);
    }

    [Fact]
    public void Drivers_AreOrderedByClassThenName()
    {
        DriverOverview overview = DriverOverview.From(
        [
            Driver(DriverClass.Sound, "Speaker"),
            Driver(DriverClass.Network, "Wi-Fi"),
            Driver(DriverClass.Display, "Beta"),
            Driver(DriverClass.Display, "Alpha"),
        ]);

        Assert.Equal(["Alpha", "Beta", "Wi-Fi", "Speaker"], overview.Shown.Select(driver => driver.Name));
    }

    [Fact]
    public void NoDrivers_GiveAnEmptyOverview()
    {
        DriverOverview overview = DriverOverview.From([]);

        Assert.Empty(overview.Shown);
        Assert.Equal(0, overview.FromWindows);
    }
}
