using Vacuo.Core.Drivers;

namespace Vacuo.Core.Tests.Drivers;

public class DriverDatesTests
{
    [Theory]
    [InlineData("7-23-2026", 2026, 7, 23)]
    [InlineData("10-10-2026", 2026, 10, 10)]
    [InlineData("03/14/2025", 2025, 3, 14)]
    public void TheFormsWindowsWrites_AreRead(string text, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), DriverDates.Read(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("13-40-2026")]
    [InlineData("not a date")]
    [InlineData(null)]
    public void AnythingElse_IsNotKnown(string? text)
    {
        Assert.Null(DriverDates.Read(text));
    }
}
