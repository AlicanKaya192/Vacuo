using Vacuo.Core.Settings;

namespace Vacuo.Core.Tests.Settings;

public class LanguageRuleTests
{
    [Theory]
    [InlineData("tr-TR", "tr-TR")]
    [InlineData("tr", "tr-TR")]
    [InlineData("en-US", "en-US")]
    [InlineData("de-DE", "en-US")]
    [InlineData("ja-JP", "en-US")]
    [InlineData("", "en-US")]
    public void Automatic_IsTurkishOnlyOnATurkishWindows(string windowsLanguage, string expected)
    {
        Assert.Equal(expected, LanguageRule.Resolve(LanguageChoice.Automatic, windowsLanguage));
    }

    [Theory]
    [InlineData(LanguageChoice.Turkish, "de-DE", "tr-TR")]
    [InlineData(LanguageChoice.English, "tr-TR", "en-US")]
    public void AChoice_WinsOverWindows(LanguageChoice choice, string windowsLanguage, string expected)
    {
        Assert.Equal(expected, LanguageRule.Resolve(choice, windowsLanguage));
    }
}
