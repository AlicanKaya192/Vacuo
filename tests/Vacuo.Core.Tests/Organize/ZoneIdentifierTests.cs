using Vacuo.Core.Organize;

namespace Vacuo.Core.Tests.Organize;

public class ZoneIdentifierTests
{
    [Fact]
    public void HostUrl_GivesItsHostWithoutWww()
    {
        const string zone = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.org/page\r\nHostUrl=https://www.github.com/a/b.zip\r\n";

        Assert.Equal("github.com", ZoneIdentifier.SourceHost(zone));
    }

    [Fact]
    public void NoHostUrl_FallsBackToReferrerUrl()
    {
        Assert.Equal("example.org", ZoneIdentifier.SourceHost("[ZoneTransfer]\nZoneId=3\nReferrerUrl=https://example.org/page\n"));
    }

    [Theory]
    [InlineData("[ZoneTransfer]\nZoneId=3\n")]
    [InlineData("[ZoneTransfer]\nZoneId=3\nHostUrl=about:internet\n")]
    [InlineData("")]
    public void NoUsableUrl_GivesNoSource(string zone)
    {
        Assert.Null(ZoneIdentifier.SourceHost(zone));
    }
}
