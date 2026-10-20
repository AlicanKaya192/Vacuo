using Vacuo.Core.Maintenance;
using Vacuo.Core.Notices;
using Vacuo.Core.Organize;
using Vacuo.Core.Tests.Localization;

namespace Vacuo.Core.Tests.Notices;

public class NoticeTextsTests
{
    private const long OnePointFourGigabytes = 1_503_238_554;

    [Fact]
    public void Run_InTurkish_SaysHowMuchWentToTheRecycleBinAndHowToGetItBack()
    {
        NoticeText? text = NoticeTexts.ForMaintenance(new MaintenanceSummary(12, OnePointFourGigabytes, Failed: 0, Stopped: false), "tr-TR");

        Assert.NotNull(text);
        Assert.Equal("Otomatik Bakım", text.Title);
        Assert.Contains("12 öğe (1,4 GB) Geri Dönüşüm Kutusu'na taşındı", text.Body);
        Assert.Contains("Günlük'ten geri alabilirsin", text.Body);
        Assert.Contains("kutu boşaltılınca", text.Body);
    }

    [Fact]
    public void Run_InEnglish_UsesEnglishNumbersAndOneItem()
    {
        NoticeText? text = NoticeTexts.ForMaintenance(new MaintenanceSummary(1, OnePointFourGigabytes, Failed: 0, Stopped: false), "en-US");

        Assert.NotNull(text);
        Assert.Equal("Automatic maintenance", text.Title);
        Assert.Contains("1 item (1.4 GB) went to the Recycle Bin", text.Body);
        Assert.Contains("Journal", text.Body);
    }

    [Fact]
    public void Run_ThatSortedTheDesktop_SaysSo()
    {
        var summary = new MaintenanceSummary(0, 0, Failed: 0, Stopped: false) { Organized = 5 };

        NoticeText? text = NoticeTexts.ForMaintenance(summary, "tr-TR");

        Assert.NotNull(text);
        Assert.Contains("Masaüstünde 5 dosya klasörlere yerleşti", text.Body);
        Assert.DoesNotContain("Geri Dönüşüm", text.Body);
    }

    [Fact]
    public void Run_StoppedByTheUser_SaysTheRestWaits()
    {
        NoticeText? text = NoticeTexts.ForMaintenance(new MaintenanceSummary(3, 4096, Failed: 0, Stopped: true), "en-US");

        Assert.NotNull(text);
        Assert.Contains("stopped when you came back", text.Body);
    }

    [Fact]
    public void Run_ThatDidNothing_ShowsNoNotice()
    {
        Assert.Null(NoticeTexts.ForMaintenance(new MaintenanceSummary(0, 0, Failed: 2, Stopped: false), "tr-TR"));
    }

    [Theory]
    [InlineData("tr-TR", "Bellek sıkışık")]
    [InlineData("en-US", "Memory is tight")]
    public void TightMemory_SaysVacuoClosesNothing(string language, string title)
    {
        NoticeText text = NoticeTexts.ForTightMemory(language);

        Assert.Equal(title, text.Title);
        Assert.False(string.IsNullOrWhiteSpace(text.Body));
    }

    // The helper names the Desktop folders itself; they must be the ones Düzen makes in the panel.
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public void DesktopFolderNames_AreThePanelsNames(string language)
    {
        Dictionary<string, string> panel = ResourceParityTests.Strings(language);

        foreach (FileKind kind in Enum.GetValues<FileKind>())
        {
            Assert.Equal(panel[$"Organize_Kind_{kind}"], NoticeTexts.DesktopFolderName(kind, language));
        }
    }
}
