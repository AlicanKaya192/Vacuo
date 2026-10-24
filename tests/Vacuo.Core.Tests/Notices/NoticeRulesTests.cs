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

    private const long Gigabyte = 1L << 30;

    // Low below a tenth of the disk or below 10 GB, whichever comes first as the disk fills.
    [Theory]
    [InlineData(1000, 99, true)]
    [InlineData(1000, 100, false)]
    [InlineData(64, 9, true)]
    [InlineData(64, 10, false)]
    public void DiskSpace_IsLowBelowATenthOrTenGigabytes(long totalGigabytes, long freeGigabytes, bool low)
    {
        Assert.Equal(low, DiskSpaceNoticePolicy.IsLow(freeGigabytes * Gigabyte, totalGigabytes * Gigabyte));
    }

    [Fact]
    public void LowDisk_NeverToldBefore_IsTold()
    {
        Assert.True(DiskSpaceNoticePolicy.ShouldNotify(isLow: true, wasLow: false, lastNotice: null, Now));
    }

    [Fact]
    public void DiskNotLow_IsNotTold()
    {
        Assert.False(DiskSpaceNoticePolicy.ShouldNotify(isLow: false, wasLow: false, lastNotice: null, Now));
    }

    [Fact]
    public void DiskStillLow_IsToldOnlyOnce()
    {
        Assert.False(DiskSpaceNoticePolicy.ShouldNotify(isLow: true, wasLow: true, lastNotice: Now.AddDays(-3), Now));
    }

    [Theory]
    [InlineData(12, false)]
    [InlineData(23, false)]
    [InlineData(24, true)]
    [InlineData(72, true)]
    public void DiskLowAgain_IsToldAtMostOnceADay(int hoursSinceLastNotice, bool told)
    {
        DateTimeOffset lastNotice = Now.AddHours(-hoursSinceLastNotice);

        Assert.Equal(told, DiskSpaceNoticePolicy.ShouldNotify(isLow: true, wasLow: false, lastNotice, Now));
    }

    [Fact]
    public void LastDiskNoticeInTheFuture_ClockWasSetBack_IsTold()
    {
        Assert.True(DiskSpaceNoticePolicy.ShouldNotify(isLow: true, wasLow: false, lastNotice: Now.AddDays(1), Now));
    }
}
