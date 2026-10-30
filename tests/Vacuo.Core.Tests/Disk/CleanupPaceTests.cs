using Vacuo.Core.Disk;

namespace Vacuo.Core.Tests.Disk;

public class CleanupPaceTests
{
    private const int Planets = 7;
    private const double Precision = 1e-9;

    [Fact]
    public void Starts_with_nothing_shown()
    {
        var pace = new CleanupPace(Planets);

        Assert.Equal(0, pace.Shown);
        Assert.False(pace.IsDone);
    }

    [Fact]
    public void Follows_slow_progress_exactly()
    {
        var pace = new CleanupPace(Planets);

        pace.Report(0.3);
        pace.Advance(10);

        Assert.Equal(0.3, pace.Shown, Precision);
    }

    [Fact]
    public void Gives_every_planet_its_minimum_time_when_the_cleanup_is_fast()
    {
        var pace = new CleanupPace(Planets, minimumSecondsPerPlanet: 0.4);

        pace.Report(1);
        pace.Advance(0.4);

        Assert.Equal(1.0 / Planets, pace.Shown, Precision);
    }

    [Fact]
    public void Is_done_once_every_planet_had_its_time()
    {
        var pace = new CleanupPace(Planets, minimumSecondsPerPlanet: 0.4);

        pace.Report(1);
        pace.Advance(2.7);
        bool doneEarly = pace.IsDone;
        pace.Advance(0.1);

        Assert.False(doneEarly);
        Assert.True(pace.IsDone);
        Assert.Equal(1, pace.Shown, Precision);
    }

    [Fact]
    public void Never_goes_back_when_a_later_report_is_lower()
    {
        var pace = new CleanupPace(Planets);

        pace.Report(0.5);
        pace.Report(0.2);
        pace.Advance(10);

        Assert.Equal(0.5, pace.Shown, Precision);
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(1.5, 1)]
    [InlineData(double.NaN, 0)]
    public void Keeps_reports_between_zero_and_one(double reported, double shown)
    {
        var pace = new CleanupPace(Planets);

        pace.Report(reported);
        pace.Advance(10);

        Assert.Equal(shown, pace.Shown, Precision);
    }

    [Fact]
    public void Finish_takes_it_all_the_way()
    {
        var pace = new CleanupPace(Planets);

        pace.Report(0.2);
        pace.Finish();
        pace.Advance(10);

        Assert.True(pace.IsDone);
    }

    [Fact]
    public void Without_reports_it_creeps_in_slowly()
    {
        var pace = new CleanupPace(Planets);
        pace.ExpectNoReports();

        pace.Advance(30);
        double early = pace.Shown;
        pace.Advance(30);

        Assert.InRange(early, 0.1, 0.3);
        Assert.True(pace.Shown > early);
    }

    // However long the work takes, the last of it is never shown before it is really over.
    [Fact]
    public void Without_reports_it_never_finishes_by_itself()
    {
        var pace = new CleanupPace(Planets);
        pace.ExpectNoReports();

        pace.Advance(1_000_000);

        Assert.False(pace.IsDone);
        Assert.True(pace.Shown < 6.0 / Planets);
    }

    [Fact]
    public void Without_reports_many_small_steps_show_the_same_as_one_big_one()
    {
        var steps = new CleanupPace(Planets);
        var once = new CleanupPace(Planets);
        steps.ExpectNoReports();
        once.ExpectNoReports();

        for (int frame = 0; frame < 6000; frame++)
        {
            steps.Advance(0.01);
        }

        once.Advance(60);

        Assert.Equal(once.Shown, steps.Shown, 1e-6);
    }

    [Fact]
    public void Finish_after_no_reports_takes_it_all_the_way()
    {
        var pace = new CleanupPace(Planets);
        pace.ExpectNoReports();
        pace.Advance(30);

        pace.Finish();
        pace.Advance(10);

        Assert.True(pace.IsDone);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Needs_at_least_one_planet(int planets) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CleanupPace(planets));
}
