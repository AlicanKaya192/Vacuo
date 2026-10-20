using System.Xml.Linq;

namespace Vacuo.Core.Tests.Localization;

/// <summary>Every interface text exists in both languages (vacuo-kurallar 8), the Settings info pages included.</summary>
public class ResourceParityTests
{
    private static readonly string[] InfoPages = ["About", "Privacy", "License", "Terms"];

    private static Dictionary<string, string> Strings(string culture)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Vacuo.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("The repository root was not found.");
        }

        return XDocument.Load(Path.Combine(root, "src", "Vacuo.App", "Strings", culture, "Resources.resw"))
            .Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? string.Empty);
    }

    [Fact]
    public void TurkishAndEnglish_HaveTheSameKeys()
    {
        Assert.Equal(
            Strings("tr-TR").Keys.Order(StringComparer.Ordinal),
            Strings("en-US").Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public void EveryInfoPage_HasATitleAndABody(string culture)
    {
        Dictionary<string, string> strings = Strings(culture);

        foreach (string page in InfoPages)
        {
            Assert.False(string.IsNullOrWhiteSpace(strings.GetValueOrDefault($"Info_{page}_Title")), $"Info_{page}_Title in {culture}");
            Assert.False(string.IsNullOrWhiteSpace(strings.GetValueOrDefault($"Info_{page}_Body")), $"Info_{page}_Body in {culture}");
        }
    }
}
