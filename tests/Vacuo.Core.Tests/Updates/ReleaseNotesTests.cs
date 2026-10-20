using Vacuo.Core.Updates;

namespace Vacuo.Core.Tests.Updates;

public class ReleaseNotesTests
{
    private const string BothLanguages = "## Türkçe\r\n- Güncelleme ekranı\r\n- Düzeltmeler\r\n\r\n## English\r\n- Update screen\r\n- Fixes\r\n";

    [Fact]
    public void TurkishInterface_GetsTheTurkishPart()
    {
        Assert.Equal("- Güncelleme ekranı\n- Düzeltmeler", ReleaseNotes.For(BothLanguages, "tr-TR"));
    }

    [Fact]
    public void AnyOtherLanguage_GetsTheEnglishPart()
    {
        Assert.Equal("- Update screen\n- Fixes", ReleaseNotes.For(BothLanguages, "en-US"));
        Assert.Equal("- Update screen\n- Fixes", ReleaseNotes.For(BothLanguages, "de-DE"));
    }

    [Fact]
    public void NotesWithoutLanguageParts_AreShownWhole()
    {
        Assert.Equal("Fixes only.", ReleaseNotes.For("  Fixes only.\n", "tr-TR"));
    }

    [Fact]
    public void OnlyTheOtherLanguage_IsShownRatherThanNothing()
    {
        Assert.Equal("- Fixes", ReleaseNotes.For("## English\n- Fixes\n", "tr-TR"));
    }

    [Fact]
    public void TitleBeforeTheParts_IsLeftOut()
    {
        Assert.Equal("- Fixes", ReleaseNotes.For("# Vacuo 0.5.0\n\n## English\n- Fixes\n", "en-US"));
    }

    [Fact]
    public void EmptyNotes_StayEmpty()
    {
        Assert.Equal("", ReleaseNotes.For("", "tr-TR"));
    }
}
