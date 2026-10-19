namespace Vacuo.App.Measurement;

/// <summary>
/// Drives the panel by itself during Stage 0 measurement, so nobody has to click the
/// tray icon or move the mouse. Selected with <c>--script idle|coldstart|full</c>.
/// </summary>
internal sealed class MeasurementScript(PanelWindow panel, MeasurementLog log, Action quit)
{
    private static readonly TimeSpan IdleDuration = TimeSpan.FromSeconds(70);
    private static readonly TimeSpan ColdStartHold = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ClosedDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CycleStep = TimeSpan.FromSeconds(2);
    private const int OpenCloseCycles = 5;

    public async Task RunAsync(string mode)
    {
        switch (mode)
        {
            case "idle":
                await RunPhaseAsync("idle", IdleDuration);
                break;
            case "coldstart":
                panel.Toggle();
                await Task.Delay(ColdStartHold);
                break;
            case "full":
                await RunFullAsync();
                break;
            default:
                throw new ArgumentException($"Unknown measurement script '{mode}'.", nameof(mode));
        }

        quit();
    }

    private async Task RunFullAsync()
    {
        panel.Toggle();
        await RunPhaseAsync("open", OpenDuration);
        panel.Toggle();
        await RunPhaseAsync("closed", ClosedDuration);

        log.MarkPhase("cycles-start");
        for (int i = 0; i < OpenCloseCycles; i++)
        {
            panel.Toggle();
            await Task.Delay(CycleStep);
            panel.Toggle();
            await Task.Delay(CycleStep);
        }

        log.MarkPhase("cycles-end");
    }

    private async Task RunPhaseAsync(string name, TimeSpan duration)
    {
        log.MarkPhase($"{name}-start");
        await Task.Delay(duration);
        log.MarkPhase($"{name}-end");
    }
}
