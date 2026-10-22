using Vacuo.Core.Thermals;

namespace Vacuo.Core.Tests.Thermals;

public class GpuTemperatureRuleTests
{
    [Fact]
    public void TenthsOfADegree_BecomeDegrees()
    {
        Assert.Equal(44.9, GpuTemperatureRule.FromDeciCelsius(449));
    }

    // D3DKMT reports 0 for an adapter that has no sensor, such as many integrated graphics chips.
    [Fact]
    public void Zero_IsNotReported()
    {
        Assert.Null(GpuTemperatureRule.FromDeciCelsius(0));
    }

    [Theory]
    [InlineData(1499u, 149.9)]
    [InlineData(1500u, null)]
    [InlineData(uint.MaxValue, null)]
    public void AnImplausibleValue_IsNotReported(uint deciCelsius, double? expected)
    {
        Assert.Equal(expected, GpuTemperatureRule.FromDeciCelsius(deciCelsius));
    }

    [Fact]
    public void Hottest_IsTheWarmestAdapterThatReports()
    {
        GpuTemperature? hottest = GpuTemperatureRule.Hottest([
            new AdapterReading("Intel(R) UHD Graphics", 0),
            new AdapterReading("NVIDIA GeForce RTX 5070 Laptop GPU", 449),
            new AdapterReading("Another adapter", 380),
        ]);

        Assert.Equal(new GpuTemperature("NVIDIA GeForce RTX 5070 Laptop GPU", 44.9), hottest);
    }

    [Fact]
    public void Hottest_WhenNoAdapterReports_IsNull()
    {
        Assert.Null(GpuTemperatureRule.Hottest([new AdapterReading("Intel(R) UHD Graphics", 0)]));
        Assert.Null(GpuTemperatureRule.Hottest([]));
    }
}
