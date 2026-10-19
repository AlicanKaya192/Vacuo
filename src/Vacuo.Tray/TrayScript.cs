namespace Vacuo.Tray;

/// <summary>
/// The Stage 0 scenarios of the single-process measurement, driven from the tray:
/// <c>--script idle|coldstart|full|linger</c>. Runs on a worker and only posts to the tray window.
/// </summary>
internal sealed class TrayScript(TrayWindow window, TrayMeasurement measurement)
{
    private static readonly TimeSpan IdleDuration = TimeSpan.FromSeconds(70);
    private static readonly TimeSpan ColdStartHold = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ClosedDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CycleStep = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SettleAfterClose = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LingerObservation = TimeSpan.FromSeconds(50);
    private const int OpenCloseCycles = 5;

    public Task RunAsync(string mode) => mode switch
    {
        "idle" => RunIdleAsync(),
        "coldstart" => RunColdStartAsync(),
        "full" => RunFullAsync(),
        "linger" => RunLingerAsync(),
        _ => throw new ArgumentException($"Unknown measurement script '{mode}'.", nameof(mode)),
    };

    private async Task RunIdleAsync()
    {
        await PhaseAsync("idle", IdleDuration);
        window.PostQuit();
    }

    private async Task RunColdStartAsync()
    {
        window.PostOpenPanel();
        await Task.Delay(ColdStartHold);
        window.PostClosePanel();
        await Task.Delay(SettleAfterClose);
        window.PostQuit();
    }

    private async Task RunFullAsync()
    {
        window.PostOpenPanel();
        await PhaseAsync("open", OpenDuration);
        window.PostClosePanel();
        await PhaseAsync("closed", ClosedDuration);

        measurement.MarkPhase("cycles-start");
        for (int i = 0; i < OpenCloseCycles; i++)
        {
            window.PostOpenPanel();
            await Task.Delay(CycleStep);
            window.PostClosePanel();
            await Task.Delay(CycleStep);
        }

        measurement.MarkPhase("cycles-end");
        window.PostQuit();
    }

    // Run with a short --linger-seconds: shows that the hidden panel process really ends
    // and the memory drops back to the tray alone.
    private async Task RunLingerAsync()
    {
        window.PostOpenPanel();
        await Task.Delay(ColdStartHold);
        window.PostClosePanel();
        await PhaseAsync("after-close", LingerObservation);
        window.PostQuit();
    }

    private async Task PhaseAsync(string name, TimeSpan duration)
    {
        measurement.MarkPhase($"{name}-start");
        await Task.Delay(duration);
        measurement.MarkPhase($"{name}-end");
    }
}
