using Vacuo.Core.Organize;

namespace Vacuo.Core.Tests.Organize;

public class OrganizeClassifierTests
{
    private static readonly DateTimeOffset October = new(2026, 10, 20, 9, 30, 0, TimeSpan.FromHours(3));

    private static FolderItem Item(string name, string? sourceHost = null) => new(name, October, sourceHost);

    [Theory]
    [InlineData("report.PDF", FileKind.Documents)]
    [InlineData("holiday.jpeg", FileKind.Images)]
    [InlineData("talk.mp4", FileKind.Videos)]
    [InlineData("song.flac", FileKind.Music)]
    [InlineData("backup.7z", FileKind.Archives)]
    [InlineData("setup.msi", FileKind.Installers)]
    [InlineData("train.ipynb", FileKind.Code)]
    public void ByType_KnownExtension_GoesToItsKind(string name, FileKind kind)
    {
        Assert.Equal(new OrganizeTarget(OrganizeRule.ByType, kind.ToString()), OrganizeClassifier.TargetOf(Item(name), OrganizeRule.ByType));
    }

    [Theory]
    [InlineData("notes.unknownext")]
    [InlineData("README")]
    public void ByType_UnknownOrNoExtension_StaysWhereItIs(string name)
    {
        Assert.Null(OrganizeClassifier.TargetOf(Item(name), OrganizeRule.ByType));
    }

    [Fact]
    public void ByDate_GoesToTheMonthItWasLastChanged()
    {
        Assert.Equal(new OrganizeTarget(OrganizeRule.ByDate, "2026-10"), OrganizeClassifier.TargetOf(Item("a.txt"), OrganizeRule.ByDate));
    }

    [Fact]
    public void BySource_GoesToTheWebsiteItCameFrom()
    {
        Assert.Equal(
            new OrganizeTarget(OrganizeRule.BySource, "github.com"),
            OrganizeClassifier.TargetOf(Item("repo.zip", "github.com"), OrganizeRule.BySource));
    }

    [Fact]
    public void BySource_NoKnownSource_StaysWhereItIs()
    {
        Assert.Null(OrganizeClassifier.TargetOf(Item("repo.zip"), OrganizeRule.BySource));
    }

    [Theory]
    [InlineData("Fatura_Ekim_2026.pdf", Topic.Invoices)]
    [InlineData("elektrik faturası.pdf", Topic.Invoices)]
    [InlineData("Amazon-Receipt.pdf", Topic.Invoices)]
    [InlineData("DeepLearning_Ödev3.ipynb", Topic.School)]
    [InlineData("SINAV-notlari.docx", Topic.School)]
    [InlineData("lab 2 report.pdf", Topic.School)]
    [InlineData("Alican_CV.pdf", Topic.Work)]
    [InlineData("boarding-pass.pdf", Topic.Travel)]
    [InlineData("Ekran görüntüsü 2026-10-20 093000.png", Topic.Screenshots)]
    [InlineData("Screenshot 2026-10-20.png", Topic.Screenshots)]
    public void ByTopic_KeywordInTheName_GoesToThatTopic(string name, Topic topic)
    {
        Assert.Equal(new OrganizeTarget(OrganizeRule.ByTopic, topic.ToString()), OrganizeClassifier.TargetOf(Item(name), OrganizeRule.ByTopic));
    }

    [Theory]
    [InlineData("label.png")]
    [InlineData("example.pdf")]
    [InlineData("holiday.jpg")]
    public void ByTopic_KeywordOnlyInsideAnotherWord_StaysWhereItIs(string name)
    {
        Assert.Null(OrganizeClassifier.TargetOf(Item(name), OrganizeRule.ByTopic));
    }
}
