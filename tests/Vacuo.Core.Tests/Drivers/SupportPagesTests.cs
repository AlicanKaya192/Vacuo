using Vacuo.Core.Drivers;

namespace Vacuo.Core.Tests.Drivers;

public class SupportPagesTests
{
    [Theory]
    [InlineData("NVIDIA Corporation", "www.nvidia.com")]
    [InlineData("NVIDIA", "www.nvidia.com")]
    [InlineData("Intel(R) Corporation", "www.intel.com")]
    [InlineData("Advanced Micro Devices, Inc.", "www.amd.com")]
    [InlineData("AMD", "www.amd.com")]
    [InlineData("Realtek Semiconductor Corp.", "www.realtek.com")]
    [InlineData("ASUSTeK COMPUTER INC.", "www.asus.com")]
    [InlineData("LENOVO", "support.lenovo.com")]
    [InlineData("HP", "support.hp.com")]
    [InlineData("Hewlett-Packard", "support.hp.com")]
    [InlineData("Dell Inc.", "www.dell.com")]
    [InlineData("Acer", "www.acer.com")]
    [InlineData("Micro-Star International Co., Ltd.", "www.msi.com")]
    [InlineData("MSI", "www.msi.com")]
    [InlineData("Gigabyte Technology Co., Ltd.", "www.gigabyte.com")]
    [InlineData("ASRock", "www.asrock.com")]
    public void AKnownMaker_GetsItsOwnPage(string vendor, string host)
    {
        Uri? page = SupportPages.For(vendor);

        Assert.NotNull(page);
        Assert.Equal("https", page.Scheme);
        Assert.Equal(host, page.Host);
    }

    [Theory]
    [InlineData("Texas Instruments")]
    [InlineData("SteelSeries ApS")]
    [InlineData("Intelligent Systems")]
    [InlineData("Shpilka")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownMaker_GetsNoPage(string? vendor)
    {
        Assert.Null(SupportPages.For(vendor));
    }

    [Fact]
    public void WindowsUpdate_IsTheSettingsPage()
    {
        Assert.Equal("ms-settings:windowsupdate", SupportPages.WindowsUpdate.OriginalString);
    }
}
