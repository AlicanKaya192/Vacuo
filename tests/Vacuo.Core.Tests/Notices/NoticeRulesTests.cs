using Vacuo.Core.Notices;
using Vacuo.Core.Ram;

namespace Vacuo.Core.Tests.Notices;

public class NoticeRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 14, 0, 0, TimeSpan.FromHours(3));

    // SHQueryUserNotificationState: 1 not present, 2 busy, 3 full-screen Direct3D, 4 presentation,
    // 5 accepts notifications, 6 quiet time, 7 Store app.
    [Theory]
    [InlineData(5, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public void Notice_ShowsOnlyWhenWindowsAcceptsNotifications(int state, bool shows)
    {
        Assert.Equal(shows, NoticeGate.MayShow(state));
    }

    [Fact]
    public void TightMemory_NeverToldBefore_IsTold()
    {
        Assert.True(MemoryNoticePolicy.ShouldNotify(MemoryPressure.Tight, wasTight: false, lastNotice: null, Now));
    }

    [Theory]
    [InlineData(MemoryPressure.Comfortable)]
    [InlineData(MemoryPressure.Filling)]
    public void MemoryNotTight_IsNotTold(MemoryPressure pressure)
    {
        Assert.False(MemoryNoticePolicy.ShouldNotify(pressure, wasTight: false, lastNotice: null, Now));
    }

    [Fact]
    public void StillTight_IsToldOnlyOnce()
    {
        Assert.False(MemoryNoticePolicy.ShouldNotify(MemoryPressure.Tight, wasTight: true, lastNotice: Now.AddHours(-5), Now));
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(180, true)]
    public void TightAgain_IsToldAtMostOnceAnHour(int minutesSinceLastNotice, bool told)
    {
        DateTimeOffset lastNotice = Now.AddMinutes(-minutesSinceLastNotice);

        Assert.Equal(told, MemoryNoticePolicy.ShouldNotify(MemoryPressure.Tight, wasTight: false, lastNotice, Now));
    }

    [Fact]
    public void LastNoticeInTheFuture_ClockWasSetBack_IsTold()
    {
        Assert.True(MemoryNoticePolicy.ShouldNotify(MemoryPressure.Tight, wasTight: false, lastNotice: Now.AddDays(1), Now));
    }
}
