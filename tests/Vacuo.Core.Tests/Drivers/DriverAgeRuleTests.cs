using Vacuo.Core.Drivers;

namespace Vacuo.Core.Tests.Drivers;

public class DriverAgeRuleTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);

    private static DriverEntry Driver(string? provider, DateOnly? date) =>
        new(DriverClass.Display, "Some adapter", provider, "1.0", date);

    [Fact]
    public void ADriverOlderThanAYear_LooksOld()
    {
        Assert.True(DriverAgeRule.LooksOld(Driver("Realtek", new DateOnly(2025, 10, 29)), Today));
    }

    [Fact]
    public void ADriverExactlyAYearOld_DoesNotLookOld()
    {
        Assert.False(DriverAgeRule.LooksOld(Driver("Realtek", new DateOnly(2025, 10, 30)), Today));
    }

    [Fact]
    public void ADriverWithoutADate_DoesNotLookOld()
    {
        Assert.False(DriverAgeRule.LooksOld(Driver("Realtek", null), Today));
    }

    [Theory]
    [InlineData("Microsoft")]
    [InlineData("microsoft corporation")]
    public void WindowsOwnDrivers_NeverLookOld(string provider)
    {
        // Their dates can say 2006 on a current Windows.
        Assert.False(DriverAgeRule.LooksOld(Driver(provider, new DateOnly(2006, 6, 21)), Today));
    }

    [Theory]
    [InlineData("Microsoft", true)]
    [InlineData("MICROSOFT CORPORATION", true)]
    [InlineData("Intel Corporation", false)]
    [InlineData(null, false)]
    public void IsFromWindows_GoesByTheProvider(string? provider, bool expected)
    {
        Assert.Equal(expected, DriverAgeRule.IsFromWindows(provider));
    }
}
