using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Vacuo.App.Localization;
using Vacuo.App.Measurement;
using Vacuo.App.Visuals;
using Vacuo.Core.Formatting;
using Windows.Graphics;
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
    private const int PanelHeight = 560;
    private const int ScreenMargin = 12;
    private const int ParticleCount = 2000;
    private const double DefaultDpi = 96;
    // A tray click first deactivates the open panel; ignore the click that follows it.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CardAnimationDuration = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan CardAnimationStagger = TimeSpan.FromMilliseconds(60);
    private const float CardStartTiltDegrees = 24f;

    private readonly Localizer _localizer;
    private readonly MeasurementLog? _measurement;
    private readonly VoidField _voidField = new(ParticleCount);
    private readonly bool _animationsEnabled = new UISettings().AnimationsEnabled;
    private DateTime _hiddenAt = DateTime.MinValue;
    private bool _isOpen;
    private bool _shuttingDown;

    internal PanelWindow(Localizer localizer, MeasurementLog? measurement)
    {
        _localizer = localizer;
        _measurement = measurement;
        InitializeComponent();

        Handle = WindowNative.GetWindowHandle(this);
        ConfigureAsFlyout();
        ApplyTexts();

        Activated += OnActivated;
        AppWindow.Closing += OnClosing;
    }

    internal event EventHandler? QuitRequested;

    internal nint Handle { get; }

    internal void Toggle()
    {
        if (_isOpen)
        {
            Hide();
            return;
        }

        if (DateTime.UtcNow - _hiddenAt < ReopenGuard)
        {
            return;
        }

        Open();
    }

    internal void Shutdown()
    {
        _shuttingDown = true;
        // Stops Win2D's game-loop thread before the window goes away.
        VoidCanvas.RemoveFromVisualTree();
        Close();
    }

    private void ConfigureAsFlyout()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
    }

    private void ApplyTexts()
    {
        Tagline.Text = _localizer.Get("AppTagline");
        DiskTitle.Text = _localizer.Get("DiskTitle");
        DiskBody.Text = DescribeSystemDrive();
        MemoryTitle.Text = _localizer.Get("MemoryTitle");
        MemoryBody.Text = _localizer.Get("MemoryPlaceholder");
        OrganizeTitle.Text = _localizer.Get("OrganizeTitle");
        OrganizeBody.Text = _localizer.Get("OrganizePlaceholder");
        QuitButton.Content = _localizer.Get("QuitButton");
    }

    private string DescribeSystemDrive()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var drive = new DriveInfo(root);
        return _localizer.Format(
            "DiskFreeFormat",
            drive.Name.TrimEnd('\\'),
            ByteSizeFormatter.Format(drive.AvailableFreeSpace, _localizer.Culture),
            ByteSizeFormatter.Format(drive.TotalSize, _localizer.Culture));
    }

    private void Open()
    {
        _measurement?.MarkPanelRequested();
        DiskBody.Text = DescribeSystemDrive();
        PlaceAboveTray();
        AppWindow.Show();
        Activate();
        _isOpen = true;
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
    }

    private void PlaceAboveTray()
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = GetDpiForWindow(Handle) / DefaultDpi;
        int width = (int)(PanelWidth * scale);
        int height = (int)(PanelHeight * scale);
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
        if (args.WindowActivationState == WindowActivationState.Deactivated && _isOpen)
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

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        QuitRequested?.Invoke(this, EventArgs.Empty);
    }
}
