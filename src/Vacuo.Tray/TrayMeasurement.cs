using System.Diagnostics;
using System.Text.Json;

namespace Vacuo.Tray;

/// <summary>
/// Stage 0 measurement on the tray side: phase marks and panel launch times. Each panel
/// process writes its own file next to this one. Local JSON only, nothing is sent.
/// </summary>
internal sealed class TrayMeasurement(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Lock _gate = new();
    private readonly Dictionary<string, double> _phasesMs = [];

    public string PanelFile(int launchIndex) => Path.Combine(directory, $"panel-{launchIndex}.json");

    public void MarkPhase(string name)
    {
        double sinceStart = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        lock (_gate)
        {
            _phasesMs[name] = sinceStart;
        }
    }

    public void Save(IReadOnlyList<double> launchesMs)
    {
        lock (_gate)
        {
            var report = new { PhasesMs = _phasesMs, PanelLaunchesMs = launchesMs };
            File.WriteAllText(Path.Combine(directory, "tray.json"), JsonSerializer.Serialize(report, JsonOptions));
        }
    }
}
