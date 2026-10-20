using System.Diagnostics;
using Vacuo.Core.Settings;
using Vacuo.Core.Updates;
using Vacuo.Windows;

namespace Vacuo.App.Updates;

internal enum UpdatePhase
{
    Idle,
    Checking,
    Downloading,
    Installing,
}

/// <summary>Why the last download or setup did not go through; shown on the update screen.</summary>
internal enum UpdateProblem
{
    None,
    Cancelled,
    Offline,
    Failed,
    Rejected,
    Blocked,
    InstallFailed,
}

/// <summary>
/// The panel's updater (vacuo-kurulum-guncelleme-plani K3): checks GitHub when the panel opens and when asked
/// in Settings, downloads the newer release the moment the user presses Update, and hands over to its setup,
/// which waits for Vacuo to close, installs and opens Vacuo again. The tray never goes online. Runs on the
/// panel's UI thread; <see cref="Changed"/> tells the header, Settings and the update screen to redraw.
/// </summary>
internal sealed class UpdateSession(
    SettingsStore settings,
    UpdateStateStore state,
    ReleaseListClient releases,
    UpdateDownloader downloader,
    ReleaseVersion running,
    string downloadFolder)
{
    private CancellationTokenSource? _download;
    private SetupChoice? _choice;
    private TransferMeter _meter = new();
    private Stopwatch _clock = new();

    public event EventHandler? Changed;

    /// <summary>The setup has started; the panel quits now so it can replace Vacuo's files.</summary>
    public event EventHandler? InstallStarted;

    public ReleaseVersion Running => running;

    public UpdatePhase Phase { get; private set; }

    /// <summary>What the latest check in this run found; null before the first.</summary>
    public UpdateStatus? LastStatus { get; private set; }

    public UpdateOffer? Offer { get; private set; }

    public DownloadProgress Progress { get; private set; }

    public UpdateProblem Problem { get; private set; }

    public double? BytesPerSecond => _meter.BytesPerSecond;

    public TimeSpan? Remaining => _download is not null && _choice is { } choice ? _meter.Remaining(choice.Setup.Size) : null;

    /// <summary>What Update would download: the patch from this version unless one already failed (K4), else the full setup.</summary>
    public SetupChoice? NextSetup => Offer is { } offer
        ? offer.ChooseSetup(InstallFollowUps.PatchFailedBefore(state.LoadPatchAttempt(), offer.Version, running))
        : null;

    /// <summary>A download or setup is under way: the hidden panel's process must not end yet.</summary>
    public bool IsBusy => Phase is UpdatePhase.Downloading or UpdatePhase.Installing;

    public DateTimeOffset? LastCheckedAt => state.LoadLastCheck();

    /// <summary>Off means Vacuo never goes online (vacuo-kurallar 6), not even by hand.</summary>
    public bool CheckingEnabled => settings.Load().CheckForUpdates;

    public void SetCheckingEnabled(bool enabled)
    {
        settings.Save(settings.Load() with { CheckForUpdates = enabled });
        Raise();
    }

    /// <summary>The first start after a setup: whether it went through. Vacuo's own downloaded setups are removed either way.</summary>
    public InstallFollowUp FollowUpInstall()
    {
        InstallFollowUp followUp = InstallFollowUps.Of(state.LoadPendingInstall(), running);
        if (followUp != InstallFollowUp.None)
        {
            state.ClearPendingInstall();
            RemoveOwnDownloads();
        }

        return followUp;
    }

    public async Task CheckAsync(bool byHand)
    {
        if (Phase != UpdatePhase.Idle || !UpdateSchedule.IsDue(CheckingEnabled, byHand, state.LoadLastCheck(), DateTimeOffset.UtcNow))
        {
            return;
        }

        Phase = UpdatePhase.Checking;
        Raise();
        UpdateCheck check = await releases.CheckAsync(running, CancellationToken.None);
        if (UpdateSchedule.CountsAsChecked(check.Status))
        {
            state.SaveLastCheck(DateTimeOffset.UtcNow);
            Offer = check.Offer;
        }

        LastStatus = check.Status;
        Phase = UpdatePhase.Idle;
        Raise();
    }

    public async Task UpdateAsync(int? trayProcessId)
    {
        if (Offer is not { } offer || NextSetup is not { } choice || Phase != UpdatePhase.Idle)
        {
            return;
        }

        long size = choice.Setup.Size;
        _choice = choice;
        Phase = UpdatePhase.Downloading;
        Problem = UpdateProblem.None;
        Progress = new DownloadProgress(0, size);
        _meter = new TransferMeter();
        _clock = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource();
        _download = cancellation;
        Raise();

        // Created here, on the UI thread, so each report arrives back on it.
        var progress = new Progress<DownloadProgress>(OnProgress);
        DownloadResult result = await downloader.DownloadAsync(choice, progress, cancellation.Token);
        _download = null;
        if (result.Outcome != DownloadOutcome.Ready)
        {
            Stop(ProblemOf(result.Outcome));
            return;
        }

        Phase = UpdatePhase.Installing;
        Progress = new DownloadProgress(size, size);
        Raise();
        state.SavePendingInstall(offer.Version.ToString());
        if (choice.IsPatch)
        {
            // If the patch refuses this installation, the next Update takes the full setup.
            state.SavePatchAttempt(offer.Version.ToString());
        }
        InstallerStart start = InstallerLauncher.Start(result.InstallerPath!, InstallerCommand.Arguments(Environment.ProcessId, trayProcessId));
        if (start == InstallerStart.Started)
        {
            InstallStarted?.Invoke(this, EventArgs.Empty);
            return;
        }

        state.ClearPendingInstall();
        Stop(start == InstallerStart.Blocked ? UpdateProblem.Blocked : UpdateProblem.InstallFailed);
    }

    /// <summary>Stops the download at its next piece; the partial file is removed.</summary>
    public void Cancel() => _download?.Cancel();

    private void OnProgress(DownloadProgress progress)
    {
        // A report can arrive after the download has ended.
        if (Phase != UpdatePhase.Downloading)
        {
            return;
        }

        Progress = progress;
        _meter.Add(progress.Received, _clock.Elapsed);
        Raise();
    }

    private void Stop(UpdateProblem problem)
    {
        Phase = UpdatePhase.Idle;
        Problem = problem;
        Raise();
    }

    private static UpdateProblem ProblemOf(DownloadOutcome outcome) => outcome switch
    {
        DownloadOutcome.Cancelled => UpdateProblem.Cancelled,
        DownloadOutcome.Offline => UpdateProblem.Offline,
        DownloadOutcome.Rejected => UpdateProblem.Rejected,
        _ => UpdateProblem.Failed,
    };

    // Only the setups this updater downloaded; anything else in the folder is left alone.
    private void RemoveOwnDownloads()
    {
        try
        {
            if (!Directory.Exists(downloadFolder))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(downloadFolder).Where(file => InstallFollowUps.IsOwnDownload(Path.GetFileName(file))))
            {
                File.Delete(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Tried again after the next update.
        }
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
