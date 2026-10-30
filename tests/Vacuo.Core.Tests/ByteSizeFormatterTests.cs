using System.Globalization;
using Vacuo.Core.Formatting;

namespace Vacuo.Core.Tests;

public class ByteSizeFormatterTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Format_ZeroBytes_ReturnsZeroB()
    {
        Assert.Equal("0 B", ByteSizeFormatter.Format(0, English));
    }

    [Fact]
    public void Format_BelowOneKilobyte_ShowsWholeBytes()
    {
        Assert.Equal("512 B", ByteSizeFormatter.Format(512, English));
    }

    [Fact]
    public void Format_Kilobytes_UsesBinaryUnits()
    {
        Assert.Equal("1.5 KB", ByteSizeFormatter.Format(1536, English));
    }

    [Fact]
    public void Format_Gigabytes_UsesTurkishDecimalComma()
    {
        long bytes = (long)(4.2 * 1024 * 1024 * 1024);

        Assert.Equal("4,2 GB", ByteSizeFormatter.Format(bytes, Turkish));
    }

    [Fact]
    public void Format_Gigabytes_UsesEnglishDecimalPoint()
    {
        long bytes = (long)(4.2 * 1024 * 1024 * 1024);

        Assert.Equal("4.2 GB", ByteSizeFormatter.Format(bytes, English));
    }

    [Fact]
    public void Format_Terabytes_IsLargestUnit()
    {
        long bytes = 2048L * 1024 * 1024 * 1024 * 1024;

        Assert.Equal("2048.0 TB", ByteSizeFormatter.Format(bytes, English));
    }

    [Fact]
    public void Format_NegativeSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteSizeFormatter.Format(-1, English));
    }

    [Fact]
    public void FormatChange_Growth_HasAPlus()
    {
        Assert.Equal("+2,0 GB", ByteSizeFormatter.FormatChange(2L << 30, Turkish));
    }

    [Fact]
    public void FormatChange_Shrinking_HasAMinus()
    {
        Assert.Equal("−2.0 GB", ByteSizeFormatter.FormatChange(-(2L << 30), English));
    }

    [Fact]
    public void FormatChange_NoChange_HasNoSign()
    {
        Assert.Equal("0 B", ByteSizeFormatter.FormatChange(0, English));
    }
}
