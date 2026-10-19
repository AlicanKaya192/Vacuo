using Vacuo.Core.Ram;

namespace Vacuo.Core.Tests.Ram;

public class AppSorterTests
{
    private const long Mb = 1L << 20;
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private static RunningApp App(string name, long privateMb, bool hasWindow = true, bool isProtected = false) =>
        new(name, privateMb * Mb, hasWindow, isProtected);

    private static AppUse Single(IReadOnlyList<AppUse> sorted, string name) => sorted.Single(use => use.App.Name == name);

    [Fact]
    public void HeavyAppOutOfFrontForAnHour_IsIdle()
    {
        var history = new ForegroundHistory(Start);
        history.Switched("blender", Start);
        history.Switched("code", Start.AddMinutes(10));

        IReadOnlyList<AppUse> sorted = AppSorter.Sort([App("blender", 900), App("code", 400)], history, Start.AddMinutes(70));

        AppUse blender = Single(sorted, "blender");
        Assert.Equal(AppGroup.Idle, blender.Group);
        Assert.Equal(TimeSpan.FromMinutes(60), blender.UnusedFor);
        Assert.Equal(AppGroup.InUse, Single(sorted, "code").Group);
    }

    [Fact]
    public void RecentlyUsedApp_IsInUse()
    {
        var history = new ForegroundHistory(Start);
        history.Switched("blender", Start);
        history.Switched("code", Start.AddMinutes(50));

        IReadOnlyList<AppUse> sorted = AppSorter.Sort([App("blender", 900)], history, Start.AddMinutes(70));

        Assert.Equal(AppGroup.InUse, Single(sorted, "blender").Group);
    }

    [Fact]
    public void LightApp_IsNeverOfferedAsIdle()
    {
        var history = new ForegroundHistory(Start);

        IReadOnlyList<AppUse> sorted = AppSorter.Sort([App("notepad", 20)], history, Start.AddHours(5));

        Assert.Equal(AppGroup.InUse, Single(sorted, "notepad").Group);
    }

    [Fact]
    public void WhileTrackingIsYoung_NothingIsIdle()
    {
        // Vacuo cannot know what happened before it started watching.
        var history = new ForegroundHistory(Start);

        IReadOnlyList<AppUse> sorted = AppSorter.Sort([App("blender", 900)], history, Start.AddMinutes(10));

        Assert.Equal(AppGroup.InUse, Single(sorted, "blender").Group);
    }

    [Fact]
    public void ProtectedOrWindowlessApps_AreLocked()
    {
        var history = new ForegroundHistory(Start);

        IReadOnlyList<AppUse> sorted = AppSorter.Sort(
            [App("dwm", 300, isProtected: true), App("updater", 900, hasWindow: false)], history, Start.AddHours(5));

        Assert.All(sorted, use => Assert.Equal(AppGroup.Protected, use.Group));
    }

    [Fact]
    public void Apps_AreListedLargestFirst()
    {
        var history = new ForegroundHistory(Start);

        IReadOnlyList<AppUse> sorted = AppSorter.Sort([App("a", 100), App("b", 900), App("c", 400)], history, Start);

        Assert.Equal(["b", "c", "a"], sorted.Select(use => use.App.Name));
    }
}
