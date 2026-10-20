using Vacuo.Core.Updates;

namespace Vacuo.Core.Tests.Updates;

public class ReleaseVersionTests
{
    [Fact]
    public void SetupTag_GivesItsVersion()
    {
        Assert.True(ReleaseVersion.TryParseTag("setup-0.4.0", out ReleaseVersion? version));

        Assert.Equal("0.4.0", version!.ToString());
    }

    [Theory]
    [InlineData("v0.5.0")]
    [InlineData("0.5.0")]
    [InlineData("release-0.5.0")]
    public void TagWithoutTheSetupPrefix_IsNotARelease(string tag)
    {
        Assert.False(ReleaseVersion.TryParseTag(tag, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1..2")]
    [InlineData("0.5.0-beta")]
    [InlineData("-1.0")]
    [InlineData("0.5.x")]
    public void UnreadableNumber_IsRejected(string text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
        Assert.False(ReleaseVersion.TryParseTag("setup-" + text, out _));
    }

    [Fact]
    public void MissingParts_CountAsZero()
    {
        ReleaseVersion shorter = Parse("0.4");
        ReleaseVersion longer = Parse("0.4.0");

        Assert.Equal(0, shorter.CompareTo(longer));
    }

    [Theory]
    [InlineData("0.10.0", "0.9.0")]
    [InlineData("1.0.0", "0.99.99")]
    [InlineData("0.4.1", "0.4.0")]
    [InlineData("0.4.0.1", "0.4")]
    public void Numbers_CompareAsNumbers(string newer, string older)
    {
        Assert.True(Parse(newer).CompareTo(Parse(older)) > 0);
        Assert.True(Parse(older).CompareTo(Parse(newer)) < 0);
    }

    private static ReleaseVersion Parse(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out ReleaseVersion? version));
        return version!;
    }
}
