using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.App.Measurement;
using Vacuo.App.Screens;
using Vacuo.App.Visuals;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;
using Vacuo.Core.Formatting;
using Vacuo.Core.Scanning;
using Vacuo.Windows;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using static Vacuo.App.Tray.NativeMethods;

namespace Vacuo.App;

/// <summary>
/// The tray panel: opens just above the logo at the bottom right, hides when it loses focus.
/// </summary>
public sealed partial class PanelWindow : Window
{
    private const int PanelWidth = 360;
    private const int PanelHeight = 640;
    // The panel grows into the Disk screen (development plan, user flow step 3).
    private const int DiskScreenWidth = 720;
    private const int DiskScreenHeight = 640;
    private const int ScreenMargin = 12;
    private const int ParticleCount = 2000;
    private const double DefaultDpi = 96;
    // A tray click first deactivates the open panel; ignore the click that follows it.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CardAnimationDuration = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan CardAnimationStagger = TimeSpan.FromMilliseconds(60);
    private const float CardStartTiltDegrees = 24f;
    // One frame lays the new screen out, the next one puts it on screen.
    private const int FramesBeforeUncloak = 2;

    private readonly Localizer _localizer;
    private readonly MeasurementLog? _measurement;
    private readonly VoidField _voidField = new(ParticleCount);
    private readonly bool _animationsEnabled;
    private readonly DiskScanCache _diskScans = new(TimeProvider.System);
    private readonly OverlappedPresenter _presenter = OverlappedPresenter.Create();
    private DiskOverview? _diskOverview;
    private bool _showingDiskScreen;
    private bool _restoredSinceJournalOpened;
    private DateTime _hiddenAt = DateTime.MinValue;
    private bool _isOpen;
    private bool _shuttingDown;

    internal PanelWindow(Localizer localizer, MeasurementLog? measurement, bool forceReducedMotion)
    {
        _localizer = localizer;
        _measurement = measurement;
        _animationsEnabled = !forceReducedMotion && new UISettings().AnimationsEnabled;
        InitializeComponent();

        Handle = WindowNative.GetWindowHandle(this);
        _presenter.IsResizable = false;
        _presenter.IsMaximizable = false;
        AppWindow.SetPresenter(_presenter);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, App.IconFile));
        ColorTitleBar();
        UseFlyoutFrame();
        ApplyTexts();
        var recycleBin = new ShellRecycleBin(TimeProvider.System);
        var journal = new ActionJournal(ActionJournal.DefaultPath);
        var runner = new ActionRunner(journal, recycleBin, TimeProvider.System);
        DiskScreenView.Initialize(localizer, runner, recycleBin, _animationsEnabled);
        DiskScreenView.BackRequested += (_, _) => ShowCards();
        DiskScreenView.RescanRequested += (_, _) => RefreshDisk();
        DiskScreenView.JournalRequested += (_, _) => ShowJournal();
        JournalScreenView.Initialize(localizer, new JournalScreen(journal, runner));
        JournalScreenView.BackRequested += (_, _) => BackFromJournal();
        JournalScreenView.Restored += (_, _) => _restoredSinceJournalOpened = true;
        DiskScreenView.Moved += (_, paths) =>
        {
            _diskScans.Forget(paths);
            UpdateDiskCard(_diskScans.Latest!);
        };

        Activated += OnActivated;
        AppWindow.Closing += OnClosing;
    }

    internal event EventHandler? QuitRequested;

    internal event EventHandler? Hidden;

    internal nint Handle { get; }

    internal bool IsOpen => _isOpen;

    internal void Toggle()
    {
        if (_isOpen && !_showingDiskScreen)
        {
            Hide();
            return;
        }

        if (!_isOpen && DateTime.UtcNow - _hiddenAt < ReopenGuard)
        {
            return;
        }

        Reveal();
    }

    /// <summary>Opens the panel, or brings the Disk screen back when it is minimised or behind other windows.</summary>
    internal void Reveal()
    {
        if (!_isOpen)
        {
            Open();
            return;
        }

        if (_presenter.State == OverlappedPresenterState.Minimized)
        {
            _presenter.Restore();
        }

        Activate();
    }

    internal void Shutdown()
    {
        _shuttingDown = true;
        // Stops Win2D's game-loop thread before the window goes away.
        VoidCanvas.RemoveFromVisualTree();
        DiskScreenView.Shutdown();
        Close();
    }

    // The card panel: no title bar, above everything, hides when it loses focus.
    private void UseFlyoutFrame()
    {
        _presenter.IsMinimizable = false;
        _presenter.IsAlwaysOnTop = true;
        _presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.IsShownInSwitchers = false;
    }

    // The Disk screen is work the user comes back to: an ordinary window that can be minimised,
    // stays behind other windows and shows on the taskbar.
    private void UseWindowFrame()
    {
        _presenter.IsMinimizable = true;
        _presenter.IsAlwaysOnTop = false;
        _presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: true);
        AppWindow.IsShownInSwitchers = true;
    }

    private void ColorTitleBar()
    {
        var resources = Application.Current.Resources;
        var background = (Color)resources["VoidColor"];
        var ink = (Color)resources["InkColor"];
        var inkSecondary = (Color)resources["InkSecondaryColor"];
        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonHoverBackgroundColor = (Color)resources["SurfaceRaisedColor"];
        titleBar.ForegroundColor = ink;
        titleBar.ButtonForegroundColor = ink;
        titleBar.ButtonHoverForegroundColor = ink;
        titleBar.InactiveForegroundColor = inkSecondary;
        titleBar.ButtonInactiveForegroundColor = inkSecondary;
    }

    // Hiding and showing again still flashed the last frame of the old screen. Cloaked, the
    // window keeps rendering and stays active but is not on screen; it reappears only after
    // the new screen has been drawn.
    private void Reshape(Action reshape)
    {
        if (!AppWindow.IsVisible)
        {
            reshape();
            return;
        }

        SetCloaked(true);
        reshape();
        int frames = 0;
        void UncloakWhenDrawn(object? sender, object e)
        {
            if (++frames < FramesBeforeUncloak)
            {
                return;
            }

            CompositionTarget.Rendering -= UncloakWhenDrawn;
            SetCloaked(false);
        }

        CompositionTarget.Rendering += UncloakWhenDrawn;
    }

    private void SetCloaked(bool cloaked)
    {
        int value = cloaked ? 1 : 0;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(Handle, DwmWindowAttributeCloak, ref value, sizeof(int)));
    }

    private void ApplyTexts()
    {
        Tagline.Text = _localizer.Get("AppTagline");
        DiskTitle.Text = _localizer.Get("DiskTitle");
        DiskBody.Text = DescribeSystemDrive();
        DiskFreeLabel.Text = _localizer.Get("DiskFreeLabel");
        DiskInspectButton.Content = _localizer.Get("DiskInspectButton");
        MemoryTitle.Text = _localizer.Get("MemoryTitle");
        MemoryBody.Text = _localizer.Get("MemoryPlaceholder");
        OrganizeTitle.Text = _localizer.Get("OrganizeTitle");
        OrganizeBody.Text = _localizer.Get("OrganizePlaceholder");
        QuitButton.Content = _localizer.Get("QuitButton");
        string hideLabel = _localizer.Get("HideButton");
        ToolTipService.SetToolTip(HideButton, hideLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HideButton, hideLabel);
    }

    private static DriveInfo SystemDrive() => new(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");

    private string DescribeSystemDrive()
    {
        DriveInfo drive = SystemDrive();
        return _localizer.Format(
            "DiskFreeFormat",
            drive.Name.TrimEnd('\\'),
            ByteSizeFormatter.Format(drive.AvailableFreeSpace, _localizer.Culture),
            ByteSizeFormatter.Format(drive.TotalSize, _localizer.Culture));
    }

    private void Open()
    {
        _measurement?.MarkPanelRequested();
        ShowCards();
        AppWindow.Show();
        Activate();
        _isOpen = true;
        if (_diskScans.IsStale)
        {
            RefreshDisk();
        }
        else
        {
            ShowDiskResult(_diskScans.Latest!);
        }

        VoidCanvas.Paused = !_animationsEnabled;
        if (_animationsEnabled)
        {
            AnimateCardsIn();
        }
        else
        {
            // Animation effects are off in Windows settings: draw one still frame.
            VoidCanvas.Invalidate();
        }
    }

    private void Hide()
    {
        VoidCanvas.Paused = true;
        AppWindow.Hide();
        _isOpen = false;
        _hiddenAt = DateTime.UtcNow;
        _measurement?.MarkPanelHidden();
        Hidden?.Invoke(this, EventArgs.Empty);
    }

    private void ShowCards()
    {
        Reshape(() =>
        {
            _showingDiskScreen = false;
            DiskScreenView.Visibility = Visibility.Collapsed;
            JournalScreenView.Visibility = Visibility.Collapsed;
            VoidCanvas.Visibility = Visibility.Visible;
            Cards.Visibility = Visibility.Visible;
            UseFlyoutFrame();
            PlaceAboveTray(PanelWidth, PanelHeight);
        });

        VoidCanvas.Paused = !_isOpen || !_animationsEnabled;
        if (_isOpen && !_animationsEnabled)
        {
            VoidCanvas.Invalidate();
        }
    }

    private void ShowDiskScreen()
    {
        Reshape(() =>
        {
            _showingDiskScreen = true;
            VoidCanvas.Paused = true;
            VoidCanvas.Visibility = Visibility.Collapsed;
            Cards.Visibility = Visibility.Collapsed;
            JournalScreenView.Visibility = Visibility.Collapsed;
            DiskScreenView.Visibility = Visibility.Visible;
            UseWindowFrame();
            PlaceAboveTray(DiskScreenWidth, DiskScreenHeight);
        });

        if (_diskScans.Latest is { } latest)
        {
            DiskScreenView.Show(latest);
        }
        else
        {
            DiskScreenView.ShowScanning();
        }
    }

    // The Journal opens in the same window as the Disk screen; its frame and size stay.
    private void ShowJournal()
    {
        _restoredSinceJournalOpened = false;
        DiskScreenView.Visibility = Visibility.Collapsed;
        JournalScreenView.Visibility = Visibility.Visible;
        JournalScreenView.Show();
    }

    // Put-back items are on disk again, so the Disk screen scans before it lists anything.
    private void BackFromJournal()
    {
        JournalScreenView.Visibility = Visibility.Collapsed;
        DiskScreenView.Visibility = Visibility.Visible;
        if (_restoredSinceJournalOpened)
        {
            RefreshDisk();
        }
    }

    // The scan only reads; any failure is shown on the card instead of closing the panel.
    private async void RefreshDisk()
    {
        DiskBody.Text = _localizer.Get("DiskScanning");
        if (_showingDiskScreen)
        {
            DiskScreenView.ShowScanning();
        }

        try
        {
            ShowDiskResult(await _diskScans.ScanAsync());
        }
        catch (Exception exception)
        {
            string message = _localizer.Format("DiskScanFailedFormat", exception.Message);
            DiskBody.Text = message;
            DiskScreenView.ShowMessage(message);
        }
    }

    private void ShowDiskResult(ScanSummary summary)
    {
        UpdateDiskCard(summary);
        if (_showingDiskScreen)
        {
            DiskScreenView.Show(summary);
        }
    }

    private void UpdateDiskCard(ScanSummary summary)
    {
        DriveInfo drive = SystemDrive();
        _diskOverview = DiskOverview.From(summary, drive.RootDirectory.FullName, drive.AvailableFreeSpace, drive.TotalSize);
        DiskFreeText.Text = DiskTexts.Size(_localizer, _diskOverview.FreeBytes);
        DiskBody.Text = DescribeSystemDrive();
        ShowLegend(_diskOverview);
        DiskRingCanvas.Invalidate();
    }

    private void ShowLegend(DiskOverview overview)
    {
        DiskLegend.Children.Clear();
        foreach (DiskSegment segment in overview.Segments)
        {
            // Name and size on separate lines: "Önbellek ve geçici · 11,9 GB" does not fit the card.
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(DiskRing.ColorOf(segment.Bucket)),
            });
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = DiskTexts.BucketName(_localizer, segment.Bucket),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["InkSecondaryBrush"],
            });
            text.Children.Add(new TextBlock
            {
                Text = DiskTexts.Size(_localizer, segment.Bytes),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["InkBrush"],
            });
            row.Children.Add(text);
            DiskLegend.Children.Add(row);
        }
    }

    private void PlaceAboveTray(int logicalWidth, int logicalHeight)
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = GetDpiForWindow(Handle) / DefaultDpi;
        int width = (int)(logicalWidth * scale);
        int height = (int)(logicalHeight * scale);
        int margin = (int)(ScreenMargin * scale);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + workArea.Width - width - margin,
            workArea.Y + workArea.Height - height - margin,
            width,
            height));
    }

    // Cards tip forward out of the void, one after another; runs on the composition thread.
    private void AnimateCardsIn()
    {
        Compositor compositor = ElementCompositionPreview.GetElementVisual(Root).Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.7f), new Vector2(0.2f, 1f));
        UIElement[] cards = [DiskCard, MemoryCard, OrganizeCard];

        for (int i = 0; i < cards.Length; i++)
        {
            UIElement card = cards[i];
            card.RotationAxis = Vector3.UnitX;
            card.CenterPoint = new Vector3((float)card.ActualSize.X / 2, (float)card.ActualSize.Y, 0);

            var tilt = compositor.CreateScalarKeyFrameAnimation();
            tilt.Target = nameof(UIElement.Rotation);
            tilt.InsertKeyFrame(0f, CardStartTiltDegrees);
            tilt.InsertKeyFrame(1f, 0f, easing);
            tilt.Duration = CardAnimationDuration;
            tilt.DelayTime = CardAnimationStagger * i;
            tilt.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            card.StartAnimation(tilt);
        }
    }

    private void OnVoidUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        _voidField.Advance(args.Timing.ElapsedTime);
    }

    private void OnVoidDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        _voidField.Draw(args.DrawingSession, sender.Size.ToVector2());
        _measurement?.RecordFrame(_voidField.Count);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        // Only the card panel closes on losing focus; the Disk screen stays like any other window.
        if (args.WindowActivationState == WindowActivationState.Deactivated && _isOpen && !_showingDiskScreen)
        {
            Hide();
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shuttingDown)
        {
            return;
        }

        // Alt+F4 only hides the panel; Vacuo keeps running in the tray.
        args.Cancel = true;
        Hide();
    }

    private void OnDiskRingDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        DiskRing.Draw(args.DrawingSession, sender.Size.ToVector2(), _diskOverview);
    }

    private void OnDiskInspectClick(object sender, RoutedEventArgs e) => ShowDiskScreen();

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        QuitRequested?.Invoke(this, EventArgs.Empty);
    }
}
