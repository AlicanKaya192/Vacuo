using Vacuo.Core.Drivers;

namespace Vacuo.Core.Tests.Drivers;

public class BiosInfoTests
{
    [Fact]
    public void TheMakerAndModel_ComeFromTheSystemWhenItNamesThem()
    {
        BiosInfo bios = BiosInfo.From("1.2", "03/14/2025", "American Megatrends", "ASUSTeK", "Board Co", "Laptop 15", "Board 1");

        Assert.Equal("1.2", bios.Version);
        Assert.Equal(new DateOnly(2025, 3, 14), bios.Date);
        Assert.Equal("American Megatrends", bios.Vendor);
        Assert.Equal("ASUSTeK", bios.Manufacturer);
        Assert.Equal("Laptop 15", bios.Model);
    }

    [Theory]
    [InlineData("System manufacturer", "System Product Name")]
    [InlineData("To be filled by O.E.M.", "To Be Filled By O.E.M.")]
    [InlineData("Default string", "Default string")]
    [InlineData("  ", "")]
    [InlineData(null, null)]
    public void APlaceholder_FallsBackToTheBoard(string? system, string? product)
    {
        BiosInfo bios = BiosInfo.From("1.2", null, null, system, "MSI", product, "B650");

        Assert.Equal("MSI", bios.Manufacturer);
        Assert.Equal("B650", bios.Model);
    }

    [Fact]
    public void NothingNamed_StaysNotKnown()
    {
        BiosInfo bios = BiosInfo.From(null, null, null, null, "Default string", null, null);

        Assert.Null(bios.Version);
        Assert.Null(bios.Date);
        Assert.Null(bios.Manufacturer);
        Assert.Null(bios.Model);
    }
}
