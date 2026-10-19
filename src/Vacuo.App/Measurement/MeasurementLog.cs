using System.Diagnostics;
using System.Text.Json;

namespace Vacuo.App.Measurement;

/// <summary>
/// Stage 0 measurement: cold start, panel open latency and frame intervals.
/// Only active with <c>--measure</c>; writes a local JSON file, never sends anything.
/// </summary>
internal sealed class MeasurementLog(string path)
{
    private const int MaxFrames = 20_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Lock _gate = new();
    private readonly List<double> _frameIntervalsMs = [];
    private readonly List<double> _panelOpenMs = [];
    private double? _coldStartMs;
    private long _panelRequestedAt;
    private long _lastFrameAt;
    private int _particleCount;

    public void MarkPanelRequested()
    {
        lock (_gate)
        {
            _panelRequestedAt = Stopwatch.GetTimestamp();
            _lastFrameAt = 0;
        }
    }

    public void MarkPanelHidden()
    {
        lock (_gate)
        {
            _lastFrameAt = 0;
        }
    }

    public void RecordFrame(int particleCount)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _coldStartMs ??= (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;

            if (_panelRequestedAt != 0)
            {
                _panelOpenMs.Add(Stopwatch.GetElapsedTime(_panelRequestedAt, now).TotalMilliseconds);
                _panelRequestedAt = 0;
            }
            else if (_lastFrameAt != 0 && _frameIntervalsMs.Count < MaxFrames)
            {
                _frameIntervalsMs.Add(Stopwatch.GetElapsedTime(_lastFrameAt, now).TotalMilliseconds);
            }

            _lastFrameAt = now;
            _particleCount = particleCount;
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            var report = new
            {
                ColdStartToFirstFrameMs = _coldStartMs,
                PanelOpenToFirstFrameMs = _panelOpenMs,
                LastParticleCount = _particleCount,
                FrameIntervalsMs = _frameIntervalsMs,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
        }
    }
}
