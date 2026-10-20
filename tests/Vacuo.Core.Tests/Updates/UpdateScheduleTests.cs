using Vacuo.Core.Updates;

namespace Vacuo.Core.Tests.Updates;

public class UpdateScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TurnedOff_IsNeverDue_EvenByHand()
    {
        Assert.False(UpdateSchedule.IsDue(enabled: false, byHand: false, lastCheck: null, Now));
        Assert.False(UpdateSchedule.IsDue(enabled: false, byHand: true, lastCheck: null, Now));
    }

    [Fact]
    public void ByHand_IsDueRightAfterACheck()
    {
        Assert.True(UpdateSchedule.IsDue(enabled: true, byHand: true, lastCheck: Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void NeverChecked_IsDue()
    {
        Assert.True(UpdateSchedule.IsDue(enabled: true, byHand: false, lastCheck: null, Now));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(-359, false)]
    [InlineData(-360, true)]
    [InlineData(-2000, true)]
    public void PanelOpening_ChecksAtMostEverySixHours(int minutesAgo, bool due)
    {
        Assert.Equal(due, UpdateSchedule.IsDue(enabled: true, byHand: false, lastCheck: Now.AddMinutes(minutesAgo), Now));
    }

    [Fact]
    public void LastCheckInTheFuture_IsDue()
    {
        // The clock was set back; waiting for it to catch up could mean days without a check.
        Assert.True(UpdateSchedule.IsDue(enabled: true, byHand: false, lastCheck: Now.AddDays(3), Now));
    }

    [Theory]
    [InlineData(UpdateStatus.Newer, true)]
    [InlineData(UpdateStatus.Current, true)]
    [InlineData(UpdateStatus.Offline, false)]
    [InlineData(UpdateStatus.Failed, false)]
    public void OnlyAnAnswerFromGitHub_CountsAsChecked(UpdateStatus status, bool counts)
    {
        Assert.Equal(counts, UpdateSchedule.CountsAsChecked(status));
    }
}
