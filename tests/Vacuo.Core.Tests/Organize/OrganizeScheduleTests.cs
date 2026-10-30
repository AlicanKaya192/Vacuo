using Vacuo.Core.Notices;
using Vacuo.Core.Organize;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Organize;

public class OrganizeScheduleTests
{
    private const string Downloads = @"C:\Users\Someone\Downloads";
    private const string Bills = @"C:\Users\Someone\Faturalar";

    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static OrganizeSchedule Schedule(ScheduleEvery every, DateTimeOffset lastAsked, string folder = Downloads) =>
        new(folder, OrganizeRule.ByType, every, lastAsked);

    private static FolderItem File(string name) => new(name, Now, null);

    [Fact]
    public void Monthly_IsDueAMonthLater()
    {
        Assert.True(OrganizeSchedulePolicy.IsDue(Schedule(ScheduleEvery.Monthly, Now.AddMonths(-1)), Now));
    }

    [Fact]
    public void Monthly_IsNotDueADayEarlier()
    {
        Assert.False(OrganizeSchedulePolicy.IsDue(Schedule(ScheduleEvery.Monthly, Now.AddMonths(-1).AddDays(1)), Now));
    }

    [Fact]
    public void Weekly_IsDueSevenDaysLater()
    {
        Assert.True(OrganizeSchedulePolicy.IsDue(Schedule(ScheduleEvery.Weekly, Now.AddDays(-7)), Now));
    }

    [Fact]
    public void Weekly_IsNotDueSixDaysLater()
    {
        Assert.False(OrganizeSchedulePolicy.IsDue(Schedule(ScheduleEvery.Weekly, Now.AddDays(-6)), Now));
    }

    [Fact]
    public void ALastAskedInTheFuture_IsDue_SoASetBackClockCannotSilenceIt()
    {
        Assert.True(OrganizeSchedulePolicy.IsDue(Schedule(ScheduleEvery.Monthly, Now.AddDays(1)), Now));
    }

    [Fact]
    public void Due_LeavesOutTheOnesNotDue_AndPutsTheLongestWaitingFirst()
    {
        OrganizeSchedule recent = Schedule(ScheduleEvery.Weekly, Now.AddDays(-8), Downloads);
        OrganizeSchedule waiting = Schedule(ScheduleEvery.Weekly, Now.AddDays(-20), Bills);
        OrganizeSchedule notYet = Schedule(ScheduleEvery.Monthly, Now.AddDays(-2), @"C:\Elsewhere");

        Assert.Equal([waiting, recent], OrganizeSchedulePolicy.Due([recent, notYet, waiting], Now));
    }

    [Fact]
    public void Pending_CountsWhatTheRuleWouldMove()
    {
        Assert.Equal(2, OrganizeSchedulePolicy.Pending([File("a.pdf"), File("b.png"), File("c.unknownkind")], OrganizeRule.ByType));
    }

    [Fact]
    public void Pending_IsZeroWhenNothingFitsTheRule()
    {
        Assert.Equal(0, OrganizeSchedulePolicy.Pending([File("c.unknownkind")], OrganizeRule.ByType));
    }

    [Fact]
    public void NoFile_MeansNoSchedules()
    {
        using var tree = new TempTree();
        var store = new OrganizeScheduleStore(tree.PathOf(@"Vacuo\organize-schedules.json"));

        Assert.Empty(store.Load());
        Assert.Null(store.LatestAsked());
    }

    [Fact]
    public void ANewSchedule_StartsCountingFromNow()
    {
        using var tree = new TempTree();
        var store = new OrganizeScheduleStore(tree.PathOf(@"Vacuo\organize-schedules.json"));

        store.Set(Downloads, OrganizeRule.ByDate, ScheduleEvery.Monthly, Now);

        OrganizeSchedule saved = Assert.Single(store.Load());
        Assert.Equal(new OrganizeSchedule(Downloads, OrganizeRule.ByDate, ScheduleEvery.Monthly, Now), saved);
        Assert.False(OrganizeSchedulePolicy.IsDue(saved, Now));
    }

    [Fact]
    public void ChangingASchedule_KeepsItsLastAsked_AndAddsNoSecondOne()
    {
        using var tree = new TempTree();
        var store = new OrganizeScheduleStore(tree.PathOf(@"Vacuo\organize-schedules.json"));
        store.Set(Downloads, OrganizeRule.ByDate, ScheduleEvery.Monthly, Now.AddDays(-5));

        store.Set(Downloads.ToUpperInvariant() + @"\", OrganizeRule.ByType, ScheduleEvery.Weekly, Now);

        Assert.Equal(new OrganizeSchedule(Downloads, OrganizeRule.ByType, ScheduleEvery.Weekly, Now.AddDays(-5)), Assert.Single(store.Load()));
    }

    [Fact]
    public void Removing_LeavesTheOthers()
    {
        using var tree = new TempTree();
        var store = new OrganizeScheduleStore(tree.PathOf(@"Vacuo\organize-schedules.json"));
        store.Set(Downloads, OrganizeRule.ByType, ScheduleEvery.Monthly, Now);
        store.Set(Bills, OrganizeRule.ByType, ScheduleEvery.Monthly, Now);

        store.Remove(Downloads.ToLowerInvariant());

        Assert.Equal(Bills, Assert.Single(store.Load()).Folder);
    }

    [Fact]
    public void MarkingChecked_MovesTheDateButNamesNoFolderToOpen()
    {
        using var tree = new TempTree();
        var store = new OrganizeScheduleStore(tree.PathOf(@"Vacuo\organize-schedules.json"));
        store.Set(Downloads, OrganizeRule.ByType, ScheduleEvery.Weekly, Now.AddDays(-10));

        store.MarkChecked(Downloads, Now);

        Assert.Equal(Now, Assert.Single(store.Load()).LastAsked);
        Assert.Null(store.LatestAsked());
    }

    [Fact]
    public void MarkingAsked_RemembersWhichFolderTheNoticeWasAbout()
    {
        using var tree = new TempTree();
        var store = new OrganizeScheduleStore(tree.PathOf(@"Vacuo\organize-schedules.json"));
        store.Set(Downloads, OrganizeRule.ByDate, ScheduleEvery.Weekly, Now.AddDays(-10));

        store.MarkAsked(Downloads, Now);

        Assert.Equal(Now, Assert.Single(store.Load()).LastAsked);
        Assert.Equal(Downloads, store.LatestAsked()!.Folder);
    }

    [Fact]
    public void RemovingTheLatestAsked_LeavesNothingToOpen()
    {
        using var tree = new TempTree();
        var store = new OrganizeScheduleStore(tree.PathOf(@"Vacuo\organize-schedules.json"));
        store.Set(Downloads, OrganizeRule.ByType, ScheduleEvery.Weekly, Now);
        store.MarkAsked(Downloads, Now);

        store.Remove(Downloads);

        Assert.Null(store.LatestAsked());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"Schedules\":null}")]
    [InlineData("{\"Schedules\":[null]}")]
    public void ADamagedFile_MeansNoSchedules(string content)
    {
        using var tree = new TempTree();
        string path = tree.PathOf("organize-schedules.json");
        System.IO.File.WriteAllText(path, content);

        Assert.Empty(new OrganizeScheduleStore(path).Load());
    }

    [Theory]
    [InlineData("BySmell", "Monthly")]
    [InlineData("ByType", "Hourly")]
    public void ARowWithAnUnknownRuleOrPeriod_IsDropped_AndTheOthersStay(string rule, string every)
    {
        using var tree = new TempTree();
        string path = tree.PathOf("organize-schedules.json");
        string row = $"{{\"Folder\":\"C:\\\\Bad\",\"Rule\":\"{rule}\",\"Every\":\"{every}\",\"LastAsked\":\"2026-10-01T00:00:00+00:00\"}}";
        string good = "{\"Folder\":\"C:\\\\Good\",\"Rule\":\"ByDate\",\"Every\":\"Weekly\",\"LastAsked\":\"2026-10-01T00:00:00+00:00\"}";
        System.IO.File.WriteAllText(path, $"{{\"Schedules\":[{row},{good}]}}");

        Assert.Equal(@"C:\Good", Assert.Single(new OrganizeScheduleStore(path).Load()).Folder);
    }

    [Fact]
    public void TheNotice_NamesTheFolderAndCount_AndPromisesNothingMovesByItself()
    {
        NoticeText notice = NoticeTexts.ForOrganizeDue(Bills, 12, "tr-TR");

        Assert.Contains("Faturalar", notice.Body);
        Assert.Contains("12", notice.Body);
        Assert.Contains("onaylamadan", notice.Body);
    }

    [Fact]
    public void TheNotice_SpeaksEnglishToo_AndCountsOneFileRight()
    {
        NoticeText notice = NoticeTexts.ForOrganizeDue(Bills, 1, "en-US");

        Assert.Contains("1 file ", notice.Body);
        Assert.DoesNotContain("files", notice.Body);
    }

    [Theory]
    [InlineData("tr-TR", "İndirilenler")]
    [InlineData("en-US", "Downloads")]
    public void TheDownloadsFolder_GoesByTheNameDuzenGivesIt(string language, string name)
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        Assert.StartsWith(name + ":", NoticeTexts.ForOrganizeDue(downloads, 3, language).Body);
    }
}
