namespace Vacuo.Core.Updates;

/// <summary>
/// Speed and time left for the update screen, from the bytes received so far. The speed is measured over
/// the last <see cref="Window"/> only, so a download that speeds up or slows down shows it within seconds.
/// </summary>
public sealed class TransferMeter
{
    public static TimeSpan Window { get; } = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MinimumSpan = TimeSpan.FromSeconds(1);
    private const double FineStepSeconds = 5;
    private const double CoarseStepSeconds = 60;

    private readonly Queue<(long Received, TimeSpan At)> _samples = new();
    private long _received;

    /// <summary>Null until a second has been measured.</summary>
    public double? BytesPerSecond { get; private set; }

    public void Add(long received, TimeSpan at)
    {
        _received = received;
        _samples.Enqueue((received, at));
        while (_samples.Count > 2 && at - _samples.Peek().At > Window)
        {
            _samples.Dequeue();
        }

        (long firstReceived, TimeSpan firstAt) = _samples.Peek();
        TimeSpan span = at - firstAt;
        BytesPerSecond = span < MinimumSpan ? null : (received - firstReceived) / span.TotalSeconds;
    }

    /// <summary>Null while the speed is unknown or nothing arrives.</summary>
    public TimeSpan? Remaining(long total) => BytesPerSecond is > 0 and { } rate
        ? TimeSpan.FromSeconds(Math.Max(0, total - _received) / rate)
        : null;

    /// <summary>Up to a minute in steps of five seconds, then in whole minutes; always rounded up.</summary>
    public static TimeSpan RoundForDisplay(TimeSpan remaining)
    {
        double seconds = remaining.TotalSeconds;
        double step = seconds <= CoarseStepSeconds ? FineStepSeconds : CoarseStepSeconds;
        return TimeSpan.FromSeconds(Math.Max(step, Math.Ceiling(seconds / step) * step));
    }
}
