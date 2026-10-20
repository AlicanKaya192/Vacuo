using Vacuo.Core.Ram;

namespace Vacuo.Core.Tests.Ram;

public class MemoryScreenTests
{
    private const long Mb = 1L << 20;
    private const long Gb = 1L << 30;
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly MemorySnapshot Calm = new(32 * Gb, 14 * Gb, 21 * Gb, 34 * Gb, PageReadsPerSecond: 0);

    private static RunningApp App(string name, long privateMb, bool hasWindow = true, bool isProtected = false) =>
        new(name, privateMb * Mb, hasWindow, isProtected);

    private static MemorySection Section(MemoryScreen screen, AppGroup group) => screen.Sections.Single(section => section.Group == group);

    [Fact]
    public void Sections_AreIdleInUseProtected_EvenWhenEmpty()
    {
        MemoryScreen screen = MemoryScreen.From(Calm, [], new ForegroundHistory(Start), Start);

        Assert.Equal([AppGroup.Idle, AppGroup.InUse, AppGroup.Protected], screen.Sections.Select(section => section.Group));
        Assert.All(screen.Sections, section => Assert.Empty(section.Shown));
    }

    [Fact]
    public void AppsAreListedInTheirGroup_LargestFirst()
    {
        var history = new ForegroundHistory(Start);
        history.Switched("code", Start.AddHours(2));

        MemoryScreen screen = MemoryScreen.From(
            Calm,
            [App("brave", 300), App("blender", 900), App("code", 600), App("steamwebhelper", 400, hasWindow: false)],
            history,
            Start.AddHours(3));

        Assert.Equal(["blender", "brave"], Section(screen, AppGroup.Idle).Shown.Select(use => use.App.Name));
        Assert.Equal(["code"], Section(screen, AppGroup.InUse).Shown.Select(use => use.App.Name));
        Assert.Equal(["steamwebhelper"], Section(screen, AppGroup.Protected).Shown.Select(use => use.App.Name));
    }

    [Fact]
    public void SmallApps_AreSummedAsOthers_AndCountInTheTotal()
    {
        var history = new ForegroundHistory(Start);
        history.Switched("code", Start);

        MemoryScreen screen = MemoryScreen.From(
            Calm, [App("code", 600), App("notepad", 20), App("calc", 30)], history, Start.AddMinutes(1));

        MemorySection inUse = Section(screen, AppGroup.InUse);
        Assert.Equal(["code"], inUse.Shown.Select(use => use.App.Name));
        Assert.Equal(2, inUse.OthersCount);
        Assert.Equal(50 * Mb, inUse.OthersBytes);
        Assert.Equal(650 * Mb, inUse.TotalBytes);
    }

    [Fact]
    public void AppAtTheListingSize_IsShown()
    {
        MemoryScreen screen = MemoryScreen.From(
            Calm, [new RunningApp("code", MemoryScreen.ListedFromBytes, true, false)], new ForegroundHistory(Start), Start);

        Assert.Single(Section(screen, AppGroup.InUse).Shown);
        Assert.Equal(0, Section(screen, AppGroup.InUse).OthersCount);
    }

    [Fact]
    public void WithoutHistory_UsageIsUnknown_AndNothingIsIdle()
    {
        MemoryScreen screen = MemoryScreen.From(Calm, [App("blender", 900)], history: null, Start.AddHours(5));

        Assert.False(screen.HistoryKnown);
        Assert.Empty(Section(screen, AppGroup.Idle).Shown);
        AppUse blender = Assert.Single(Section(screen, AppGroup.InUse).Shown);
        Assert.Null(blender.UnusedFor);
    }

    [Fact]
    public void WithHistory_SaysSinceWhenVacuoWatches_AndWhichAppsItSawInFront()
    {
        var history = new ForegroundHistory(Start);
        history.Switched("code", Start.AddMinutes(5));
        history.Switched("brave", Start.AddMinutes(9));

        MemoryScreen screen = MemoryScreen.From(
            Calm, [App("code", 600), App("brave", 500), App("blender", 900)], history, Start.AddHours(2));

        Assert.Equal(Start, screen.WatchingSince);
        IEnumerable<AppUse> all = screen.Sections.SelectMany(section => section.Shown);
        Assert.True(all.Single(use => use.App.Name == "code").SeenInFront);
        Assert.True(all.Single(use => use.App.Name == "brave").SeenInFront);
        Assert.False(all.Single(use => use.App.Name == "blender").SeenInFront);
    }

    [Fact]
    public void Pressure_ComesFromTheSnapshot()
    {
        var tight = Calm with { CommittedBytes = 33 * Gb };

        Assert.Equal(MemoryPressure.Comfortable, MemoryScreen.From(Calm, [], null, Start).Pressure);
        Assert.Equal(MemoryPressure.Tight, MemoryScreen.From(tight, [], null, Start).Pressure);
    }
}
