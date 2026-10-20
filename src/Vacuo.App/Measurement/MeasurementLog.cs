using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private readonly Dictionary<string, double> _phasesMs = [];
    private readonly Dictionary<string, object> _memory = [];
    private double? _coldStartMs;
    private string? _error;
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

    /// <summary>Records when a phase began or ended, in milliseconds since the process started.</summary>
    public void MarkPhase(string name)
    {
        lock (_gate)
        {
            _phasesMs[name] = SinceProcessStartMs();
        }
    }

    /// <summary>The process's memory now, in MB: private bytes, working set and the managed (GC) heap.</summary>
    public void MarkMemory(string name)
    {
        using Process self = Process.GetCurrentProcess();
        var sample = new
        {
            // Stage 0's memory criteria use this one ("Working Set - Private").
            PrivateWorkingSetMb = Math.Round(PrivateWorkingSetBytes(self) / 1048576.0, 1),
            PrivateMb = Math.Round(self.PrivateMemorySize64 / 1048576.0, 1),
            WorkingSetMb = Math.Round(self.WorkingSet64 / 1048576.0, 1),
            ManagedHeapMb = Math.Round(GC.GetTotalMemory(forceFullCollection: false) / 1048576.0, 1),
            ManagedCommittedMb = Math.Round(GC.GetGCMemoryInfo().TotalCommittedBytes / 1048576.0, 1),
        };
        lock (_gate)
        {
            _memory[name] = sample;
        }
    }

    public void MarkError(Exception exception)
    {
        lock (_gate)
        {
            _error = exception.ToString();
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
            _coldStartMs ??= SinceProcessStartMs();

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
                PhasesMs = _phasesMs,
                Memory = _memory,
                Error = _error,
                PanelOpenToFirstFrameMs = _panelOpenMs,
                LastParticleCount = _particleCount,
                FrameIntervalsMs = _frameIntervalsMs,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
        }
    }

    private static long PrivateWorkingSetBytes(Process process)
    {
        var counters = new ProcessMemoryCountersEx2 { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>() };
        return GetProcessMemoryInfo(process.Handle, ref counters, counters.Size) ? (long)counters.PrivateWorkingSetSize : -1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(nint process, ref ProcessMemoryCountersEx2 counters, uint size);

    private static double SinceProcessStartMs() =>
        (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
}
