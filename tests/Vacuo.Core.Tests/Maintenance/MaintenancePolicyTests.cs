using Vacuo.Core.Maintenance;

namespace Vacuo.Core.Tests.Maintenance;

public class MaintenancePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 3, 0, 0, TimeSpan.FromHours(3));
    private static readonly MaintenanceSettings On = MaintenanceSettings.Default with { Enabled = true };
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

    [Fact]
    public void TurnedOn_IdleOnMains_NotRunToday_IsDue()
    {
        Assert.True(MaintenancePolicy.IsDue(On, lastRun: null, Idle, onMains: true, Now));
    }

    [Fact]
    public void TurnedOff_IsNeverDue()
    {
        Assert.False(MaintenancePolicy.IsDue(MaintenanceSettings.Default, lastRun: null, Idle, onMains: true, Now));
    }

    [Fact]
    public void NothingChosen_IsNotDue()
    {
        var nothing = On with { TempFiles = false, DownloadCaches = false, ProjectLeftovers = false, OrganizeDesktop = false };

        Assert.False(MaintenancePolicy.IsDue(nothing, lastRun: null, Idle, onMains: true, Now));
    }

    [Fact]
    public void OnBattery_IsNotDue()
    {
        Assert.False(MaintenancePolicy.IsDue(On, lastRun: null, Idle, onMains: false, Now));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(240, true)]
    public void OnlyAfterTenMinutesWithoutInput(int idleMinutes, bool due)
    {
        Assert.Equal(due, MaintenancePolicy.IsDue(On, lastRun: null, TimeSpan.FromMinutes(idleMinutes), onMains: true, Now));
    }

    [Fact]
    public void AlreadyRunToday_IsNotDue()
    {
        DateTimeOffset earlierToday = Now.AddHours(-2);

        Assert.False(MaintenancePolicy.IsDue(On, earlierToday, Idle, onMains: true, Now));
    }

    [Fact]
    public void RunYesterday_IsDue()
    {
        DateTimeOffset yesterdayLate = new(2026, 10, 20, 23, 50, 0, TimeSpan.FromHours(3));

        Assert.True(MaintenancePolicy.IsDue(On, yesterdayLate, Idle, onMains: true, Now));
    }

    [Fact]
    public void TheDay_IsTheLocalDay_NotUtc()
    {
        // 00:30 local is still the previous day in UTC; it was today here, so no second run.
        DateTimeOffset justAfterMidnight = new(2026, 10, 21, 0, 30, 0, TimeSpan.FromHours(3));

        Assert.False(MaintenancePolicy.IsDue(On, justAfterMidnight.ToUniversalTime(), Idle, onMains: true, Now));
    }

    [Fact]
    public void LastRunInTheFuture_IsDue()
    {
        // The clock was set back; waiting for it could mean days without maintenance.
        Assert.True(MaintenancePolicy.IsDue(On, Now.AddDays(2), Idle, onMains: true, Now));
    }

    [Fact]
    public void InputAfterTheStart_MeansTheUserIsBack()
    {
        DateTimeOffset started = Now;

        Assert.True(MaintenancePolicy.UserReturned(lastInput: started.AddSeconds(1), started));
        Assert.False(MaintenancePolicy.UserReturned(lastInput: started.AddMinutes(-20), started));
    }
}
