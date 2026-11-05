using Vacuo.Core.Scanning;

namespace Vacuo.Core.Tests.Localization;

/// <summary>The Tarayıcılar screen names every browser and every kind of cache in both languages (vacuo-kurallar 8).</summary>
public class BrowserTextsTests
{
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public void EveryBrowser_HasAName(string culture)
    {
        Dictionary<string, string> strings = ResourceParityTests.Strings(culture);

        Assert.All(BrowserCacheRules.Browsers, browser =>
            Assert.False(string.IsNullOrWhiteSpace(strings.GetValueOrDefault("Browser_" + browser.Id.Replace('-', '_'))), browser.Id));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public void EveryCacheKind_HasAName(string culture)
    {
        Dictionary<string, string> strings = ResourceParityTests.Strings(culture);

        Assert.All(Enum.GetValues<BrowserCacheKind>(), kind =>
            Assert.False(string.IsNullOrWhiteSpace(strings.GetValueOrDefault("BrowserCache_" + kind)), kind.ToString()));
    }
}
