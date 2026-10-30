namespace Vacuo.Core.Disk;

/// <summary>
/// How far the cleanup animation shows (Aşama 3j): it follows the real progress, but never faster than
/// <c>minimumSecondsPerPlanet</c> for each of its planets, so a cleanup that ends in a moment still shows the planets
/// torn apart and one that takes long is never shown ahead of what was done. Shown progress never goes back.
/// </summary>
public sealed class CleanupPace
{
    // Half of the ceiling goes in about the first minute: the slowest work (DISM) takes minutes.
    private const double DriftCeiling = 0.8;
    private const double DriftSeconds = 90;

    private readonly double _fastestPerSecond;
    private double _target;
    private bool _drifting;

    public CleanupPace(int planets, double minimumSecondsPerPlanet = 0.4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(planets, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumSecondsPerPlanet);
        _fastestPerSecond = 1.0 / (planets * minimumSecondsPerPlanet);
    }

    /// <summary>The share shown, from 0 to 1.</summary>
    public double Shown { get; private set; }

    public bool IsDone => Shown >= 1;

    /// <summary>The real share done; a lower or invalid report never takes the animation back.</summary>
    public void Report(double fraction)
    {
        if (double.IsNaN(fraction))
        {
            return;
        }

        _target = Math.Max(_target, Math.Clamp(fraction, 0, 1));
    }

    /// <summary>
    /// The work will not say how far it is: the share creeps towards <see cref="DriftCeiling"/> and stops there, so it
    /// slows the longer the work takes and the last of it is only shown once <see cref="Finish"/> is called.
    /// </summary>
    public void ExpectNoReports() => _drifting = true;

    /// <summary>The work is over, whatever was last reported: every planet goes in.</summary>
    public void Finish()
    {
        _drifting = false;
        _target = 1;
    }

    // Within a rounding error of the target it lands on it, so summed frame times still reach 1.
    public void Advance(double seconds)
    {
        seconds = Math.Max(0, seconds);
        if (_drifting)
        {
            _target += (DriftCeiling - _target) * (1 - Math.Exp(-seconds / DriftSeconds));
        }

        double next = Shown + seconds * _fastestPerSecond;
        Shown = next >= _target - 1e-9 ? _target : next;
    }
}
