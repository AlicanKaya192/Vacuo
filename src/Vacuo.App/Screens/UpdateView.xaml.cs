using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vacuo.App.Localization;
using Vacuo.App.Updates;
using Vacuo.App.Visuals;
using Vacuo.Core.Updates;
using Windows.System;

namespace Vacuo.App.Screens;

/// <summary>
/// The update screen (vacuo-kurulum-guncelleme-plani K3): the new version, its notes, and once Update is
/// pressed the download as a black hole filling its ring, with percent, megabytes, speed and time left.
/// "Keep going in the background" hides the panel while the download goes on; when it is done the setup
/// starts by itself, closes Vacuo and opens it again.
/// </summary>
public sealed partial class UpdateView : UserControl
{
    private static readonly Uri ReleasesPage = new("https://github.com/AlicanKaya192/vacuo/releases");

    private readonly UpdateHole _hole = new();
    private Localizer _localizer = null!;
    private UpdateSession _updates = null!;
    private bool _animationsEnabled;
    private bool _windowOpen;

    public UpdateView()
    {
        InitializeComponent();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => UpdateHoleRunning());
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? BackgroundRequested;

    /// <summary>Update was pressed; the panel starts it, as only it knows the tray's process.</summary>
    internal event EventHandler? UpdateRequested;

    internal void Initialize(Localizer localizer, UpdateSession updates, bool animationsEnabled)
    {
        _localizer = localizer;
        _updates = updates;
        _animationsEnabled = animationsEnabled;
        BackButton.Content = localizer.Get("DiskBackButton");
        BackgroundButton.Content = localizer.Get("UpdateBackgroundButton");
        CancelButton.Content = localizer.Get("UpdateCancelButton");
        ReleasesButton.Content = localizer.Get("UpdateReleasesButton");
        NotesTitle.Text = localizer.Get("UpdateNotesTitle");
        updates.Changed += OnUpdatesChanged;
    }

    internal void Shutdown()
    {
        _updates.Changed -= OnUpdatesChanged;
        HoleCanvas.RemoveFromVisualTree();
    }

    internal void SetWindowOpen(bool open)
    {
        _windowOpen = open;
        UpdateHoleRunning();
    }

    internal void Refresh()
    {
        UpdateOffer? offer = _updates.Offer;
        ScreenTitle.Text = offer is null
            ? _localizer.Get("UpdateScreenTitle")
            : _localizer.Format("UpdateTitleFormat", offer.Version);
        string notes = offer is null ? string.Empty : ReleaseNotes.For(offer.Notes, _localizer.Culture.Name);
        NotesText.Text = notes.Length > 0 ? notes : _localizer.Get("UpdateNoNotes");
        NotesCard.Visibility = offer is null ? Visibility.Collapsed : Visibility.Visible;

        bool downloading = _updates.Phase == UpdatePhase.Downloading;
        int percent = UpdateTexts.Percent(_updates.Progress);
        (StatusText.Text, DetailText.Text) = _updates.Phase switch
        {
            UpdatePhase.Downloading => (_localizer.Format("UpdatePercentFormat", percent), UpdateTexts.Download(_localizer, _updates)),
            UpdatePhase.Installing => (_localizer.Get("UpdateInstalling"), _localizer.Get("UpdateInstallingHint")),
            UpdatePhase.Checking => (_localizer.Get("SettingsUpdatesChecking"), string.Empty),
            _ when offer is not null => (
                _localizer.Format("UpdateReadyFormat", offer.Version),
                _localizer.Format("UpdateReadyHintFormat", _updates.Running, UpdateTexts.Size(_localizer, _updates.NextSetup!.Setup.Size))),
            _ => (_localizer.Get("SettingsUpdatesCurrent"), UpdateTexts.CheckState(_localizer, _updates)),
        };

        string? problem = UpdateTexts.Problem(_localizer, _updates.Problem);
        MessageText.Text = problem ?? string.Empty;
        MessageText.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;

        bool idle = _updates.Phase == UpdatePhase.Idle;
        PrimaryButton.Content = _localizer.Get(_updates.Problem == UpdateProblem.None ? "UpdateStartButton" : "UpdateRetryButton");
        PrimaryButton.Visibility = idle && offer is not null ? Visibility.Visible : Visibility.Collapsed;
        BackgroundButton.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        ReleasesButton.Visibility = _updates.Problem is UpdateProblem.Blocked or UpdateProblem.InstallFailed
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProgressTrack.Visibility = downloading || _updates.Phase == UpdatePhase.Installing ? Visibility.Visible : Visibility.Collapsed;
        FillProgress();

        _hole.Show(percent / 100f, downloading);
        if (!_animationsEnabled)
        {
            HoleCanvas.Invalidate();
        }
    }

    private void OnUpdatesChanged(object? sender, EventArgs e) => Refresh();

    // Only while it can be seen: on the update screen, in an open panel, with animations on.
    private void UpdateHoleRunning()
    {
        bool onView = _windowOpen && Visibility == Visibility.Visible;
        HoleCanvas.Paused = !onView || !_animationsEnabled;
        if (onView && !_animationsEnabled)
        {
            HoleCanvas.Invalidate();
        }
    }

    private void FillProgress()
    {
        double share = UpdateTexts.Percent(_updates.Progress) / 100.0;
        ProgressFill.Width = ProgressTrack.ActualWidth * share;
    }

    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e) => FillProgress();

    private void OnHoleUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args) => _hole.Advance(args.Timing.ElapsedTime);

    private void OnHoleDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args) =>
        _hole.Draw(args.DrawingSession, sender.Size.ToVector2());

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => UpdateRequested?.Invoke(this, EventArgs.Empty);

    private void OnBackgroundClick(object sender, RoutedEventArgs e) => BackgroundRequested?.Invoke(this, EventArgs.Empty);

    private void OnCancelClick(object sender, RoutedEventArgs e) => _updates.Cancel();

    private async void OnReleasesClick(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(ReleasesPage);
}
