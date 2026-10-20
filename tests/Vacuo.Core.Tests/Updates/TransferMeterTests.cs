using Vacuo.Core.Updates;

namespace Vacuo.Core.Tests.Updates;

public class TransferMeterTests
{
    [Fact]
    public void LessThanASecondMeasured_HasNoRateYet()
    {
        var meter = new TransferMeter();
        meter.Add(0, TimeSpan.Zero);
        meter.Add(500_000, TimeSpan.FromMilliseconds(600));

        Assert.Null(meter.BytesPerSecond);
        Assert.Null(meter.Remaining(10_000_000));
    }

    [Fact]
    public void SteadyDownload_GivesItsRateAndTheTimeLeft()
    {
        var meter = new TransferMeter();
        for (int second = 0; second <= 3; second++)
        {
            meter.Add(second * 1_000_000L, TimeSpan.FromSeconds(second));
        }

        Assert.Equal(1_000_000, meter.BytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(7), meter.Remaining(10_000_000));
    }

    [Fact]
    public void Rate_FollowsTheLastFewSeconds()
    {
        var meter = new TransferMeter();
        meter.Add(0, TimeSpan.Zero);
        meter.Add(10_000_000, TimeSpan.FromSeconds(10));
        meter.Add(12_000_000, TimeSpan.FromSeconds(12));
        meter.Add(14_000_000, TimeSpan.FromSeconds(14));
        meter.Add(16_000_000, TimeSpan.FromSeconds(16));

        // The first ten seconds (1 MB/s) have left the window; the last ones run at 1 MB/s too,
        // so this only shows when the window moves: here the rate stays 1 MB/s.
        Assert.Equal(1_000_000, meter.BytesPerSecond);

        meter.Add(26_000_000, TimeSpan.FromSeconds(18));
        meter.Add(36_000_000, TimeSpan.FromSeconds(20));
        meter.Add(46_000_000, TimeSpan.FromSeconds(22));

        Assert.True(meter.BytesPerSecond > 4_000_000, $"Rate was {meter.BytesPerSecond}.");
    }

    [Fact]
    public void StalledDownload_HasNoTimeLeft()
    {
        var meter = new TransferMeter();
        meter.Add(1_000_000, TimeSpan.Zero);
        meter.Add(1_000_000, TimeSpan.FromSeconds(6));

        Assert.Equal(0, meter.BytesPerSecond);
        Assert.Null(meter.Remaining(10_000_000));
    }

    [Fact]
    public void Finished_HasNothingLeft()
    {
        var meter = new TransferMeter();
        meter.Add(0, TimeSpan.Zero);
        meter.Add(5_000_000, TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.Zero, meter.Remaining(5_000_000));
    }

    [Theory]
    [InlineData(0.4, 5)]
    [InlineData(3, 5)]
    [InlineData(5, 5)]
    [InlineData(5.2, 10)]
    [InlineData(58, 60)]
    [InlineData(61, 120)]
    [InlineData(150, 180)]
    [InlineData(3599, 3600)]
    public void TimeLeft_IsShownRoundedUp(double seconds, int shownSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(shownSeconds), TransferMeter.RoundForDisplay(TimeSpan.FromSeconds(seconds)));
    }
}
