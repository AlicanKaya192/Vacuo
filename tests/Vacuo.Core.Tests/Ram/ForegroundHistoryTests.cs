using Vacuo.Core.Ram;

namespace Vacuo.Core.Tests.Ram;

public class ForegroundHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NeverInFront_IsUnusedSinceTrackingStarted()
    {
        var history = new ForegroundHistory(Start);

        Assert.Equal(Start, history.UnusedSince("blender"));
    }

    [Fact]
    public void AppInFrontNow_IsInUse()
    {
        var history = new ForegroundHistory(Start);

        history.Switched("code", Start.AddMinutes(5));

        Assert.Null(history.UnusedSince("code"));
    }

    [Fact]
    public void AppThatLeftTheFront_IsUnusedSinceItLeft()
    {
        var history = new ForegroundHistory(Start);
        history.Switched("code", Start.AddMinutes(5));

        history.Switched("chrome", Start.AddMinutes(50));

        Assert.Equal(Start.AddMinutes(50), history.UnusedSince("code"));
        Assert.Null(history.UnusedSince("chrome"));
    }

    [Fact]
    public void Names_IgnoreCase()
    {
        var history = new ForegroundHistory(Start);

        history.Switched("Code", Start.AddMinutes(5));

        Assert.Null(history.UnusedSince("code"));
    }
}
