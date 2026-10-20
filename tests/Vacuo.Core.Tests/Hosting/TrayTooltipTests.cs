using System.Text;
using Vacuo.Core.Hosting;

namespace Vacuo.Core.Tests.Hosting;

public class TrayTooltipTests
{
    [Fact]
    public void Text_ComesBackTheSame()
    {
        const string text = "Vacuo güncelleniyor · %42 · yaklaşık 3 dk kaldı";

        Assert.Equal(text, TrayTooltip.Decode(TrayTooltip.Encode(text)));
    }

    [Fact]
    public void EmptyText_MeansTheTraysOwnTooltip()
    {
        Assert.Empty(TrayTooltip.Encode(string.Empty));
        Assert.Equal(string.Empty, TrayTooltip.Decode([]));
    }

    [Fact]
    public void LongText_IsCutToWhatWindowsShows()
    {
        string decoded = TrayTooltip.Decode(TrayTooltip.Encode(new string('a', 300)))!;

        Assert.Equal(TrayTooltip.MaxChars, decoded.Length);
        Assert.EndsWith("…", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public void LineBreaks_BecomeSpaces()
    {
        Assert.Equal("Vacuo  %42", TrayTooltip.Decode(TrayTooltip.Encode("Vacuo\r\n%42")));
    }

    [Fact]
    public void OddNumberOfBytes_IsNotATooltip()
    {
        Assert.Null(TrayTooltip.Decode([0x41, 0x00, 0x42]));
    }

    [Fact]
    public void LongerThanWindowsShows_IsNotATooltip()
    {
        Assert.Null(TrayTooltip.Decode(Encoding.Unicode.GetBytes(new string('a', TrayTooltip.MaxChars + 1))));
    }

    [Fact]
    public void ControlCharacters_AreNotATooltip()
    {
        Assert.Null(TrayTooltip.Decode(Encoding.Unicode.GetBytes("Vacuo\0hidden")));
    }
}
