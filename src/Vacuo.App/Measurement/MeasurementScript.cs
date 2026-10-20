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
        // A failing step is written to the report and the app still quits, so a run never hangs.
        try
        {
            await RunScenarioAsync(mode);
        }
        catch (Exception exception)
        {
            log.MarkError(exception);
        }

        quit();
    }

    private async Task RunScenarioAsync(string mode)
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
            case "memory":
                await RunMemoryAsync();
                break;
            case "memory-cards":
                // Only the cards, for comparing --probe runs.
                log.MarkMemory("0-before-open");
                panel.Toggle();
                await Task.Delay(ScreenSettle);
                log.MarkMemory("1-cards");
                break;
            default:
                throw new ArgumentException($"Unknown measurement script '{mode}'.", nameof(mode));
        }
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

    // The panel's own memory through each screen, to find what makes it heavy; only reads.
    private async Task RunMemoryAsync()
    {
        log.MarkMemory("0-before-open");
        panel.Toggle();
        await Task.Delay(TimeSpan.FromSeconds(1));
        log.MarkMemory("1-cards-1s");
        await Task.Delay(ScreenSettle * 2);
        log.MarkMemory("2-cards-settled");
        panel.ShowDiskScreenForMeasurement();
        await Task.Delay(ScreenSettle);
        log.MarkMemory("3-disk");
        panel.ReturnToCardsForMeasurement();
        await Task.Delay(ScreenSettle);
        log.MarkMemory("4-cards");
        panel.ShowMemoryScreenForMeasurement();
        await Task.Delay(ScreenSettle);
        log.MarkMemory("5-memory");
        panel.ReturnToCardsForMeasurement();
        await Task.Delay(ScreenSettle);
        log.MarkMemory("6-cards");
        panel.Toggle();
        await Task.Delay(ScreenSettle);
        log.MarkMemory("7-hidden");
    }

    private static readonly TimeSpan ScreenSettle = TimeSpan.FromSeconds(8);

    private async Task RunPhaseAsync(string name, TimeSpan duration)
    {
        log.MarkPhase($"{name}-start");
        await Task.Delay(duration);
        log.MarkPhase($"{name}-end");
    }
}
