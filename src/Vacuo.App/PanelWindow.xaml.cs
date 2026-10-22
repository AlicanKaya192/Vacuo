using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.App.Measurement;
using Vacuo.App.Screens;
using Vacuo.App.Updates;
using Vacuo.App.Visuals;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;
using Vacuo.Core.Battery;
using Vacuo.Core.Formatting;
using Vacuo.Core.Organize;
using Vacuo.Core.Ram;
using Vacuo.Core.Thermals;
using Vacuo.Core.Settings;
using Vacuo.Core.Scanning;
using Vacuo.Core.Updates;
using Vacuo.Windows;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using VirtualKey = Windows.System.VirtualKey;
using static Vacuo.App.Tray.NativeMethods;

namespace Vacuo.App;

/// <summary>
/// The tray panel: opens just above the logo at the bottom right. The cards and the Disk, Windows
/// and Memory screens share one window that never changes its frame or size (B-140); only the
/// content moves, so a screen change stays inside the same app. On the cards it hides when it
/// loses focus; a screen stays like any other window until hidden to the tray.
/// </summary>
public sealed partial class PanelWindow : Window
{
    // Two card columns side by side (B-137); the screens use the same window.
    private const int PanelWidth = 720;
    private const int NarrowProbeWidth = 400;
    // The battery card's row in the right column; the tools card takes it on a desktop.
    private const int BatteryCardRow = 2;
    private const double ToolsSideBySideSpacing = 16;
    private const double ToolsStackedSpacing = 10;
    // The panel is as tall as its cards (vacuo-kurallar 13), never shorter than this.
    private const int MinPanelHeight = 640;
    private const double VoidHeight = 200;
    private const double MinVoidHeight = 96;
    // A screen change: what leaves is drawn into the void, then what comes grows out of it.
    private static readonly TimeSpan LeaveDuration = TimeSpan.FromMilliseconds(170);
    private const float LeaveScale = 0.92f;
    private static readonly TimeSpan ScreenFadeDuration = TimeSpan.FromMilliseconds(240);
    private const float ScreenStartScale = 0.96f;
    // The flyout's border sits outside the measured content.
    private const int FrameAllowance = 4;
    private const int RoundingAllowance = 1;
    private const int MaxMeasureShortfall = 48;
    private const int ScreenMargin = 12;
    private const double DefaultDpi = 96;
    // A tray click first deactivates the open panel; ignore the click that follows it.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CardAnimationDuration = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan CardAnimationStagger = TimeSpan.FromMilliseconds(60);
    private const float CardStartTiltDegrees = 24f;
    // One frame lays the new screen out, the next one puts it on screen.
    private const int FramesBeforeUncloak = 2;
    // Memory counters are read only while the panel is on screen (B-137).
    private static readonly TimeSpan MemoryRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PagingWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MemorySnapshotFreshFor = MemoryRefreshInterval * 2;

    private Localizer _localizer;
    private readonly MeasurementLog? _measurement;
    // Left of centre, so the sun has room on the right.
    private readonly BlackHoleScene _blackHole = new(centerShare: 0.44f, widthShare: 0.3f);
    private readonly SunHeat _sun = new();
    private readonly bool _animationsEnabled;
    private readonly DiskScanCache _diskScans = new(TimeProvider.System);
    private readonly OverlappedPresenter _presenter = OverlappedPresenter.Create();
    private readonly DispatcherQueueTimer _memoryTimer;
    private DiskOverview? _diskOverview;
    private string? _categoriesFailure;
    private bool _readingMemory;
    private MemorySnapshot? _memorySnapshot;
    private DateTime _memorySnapshotAt;
    private int _transition;
    private int _eventCount;
    // Set on the UI thread, cleared on Win2D's drawing thread.
    private volatile bool _markNextVoidFrame;
    // The Disk or Windows screen is open: an ordinary window, not the card panel.
    private bool _showingWorkScreen;
    // The control that opened the current screen; focus returns to it on the way back.
    private Control? _openedFrom;
    private int _cardPanelHeight;
    private int _measureShortfall;
    private UserControl _journalOpenedFrom = null!;
    private bool _restoredSinceJournalOpened;
    private DateTime _hiddenAt = DateTime.MinValue;
    private bool _isOpen;
    private bool _shuttingDown;
    // A screen change is animating; further clicks wait for it.
    private bool _switching;
    private int _panelWidth = PanelWidth;
    private readonly ShellRecycleBin _recycleBin;
    private readonly ActionJournal _journal;
    private readonly ActionRunner _runner;
    private DiskView? _diskScreen;
    private WindowsView? _windowsScreen;
    private MemoryView? _memoryScreen;
    private OrganizeView? _organizeScreen;
    private SettingsView? _settingsScreen;
    private BatteryView? _batteryScreen;
    private DuplicatesView? _duplicatesScreen;
    private UninstallView? _uninstallScreen;
    private JournalView? _journalScreen;
    private WeekView? _weekScreen;
    private UpdateView? _updateScreen;
    private readonly UpdateSession _updates;
    // Shown in the header during the first opening after an update.
    private InstallFollowUp _installFollowUp;
    private string _trayTooltip = string.Empty;
    private bool _pretendNoBattery;
    private const string TempProbePrefix = "temp=";
    private string? _tempProbe;

    internal PanelWindow(Localizer localizer, MeasurementLog? measurement, bool forceReducedMotion, IReadOnlySet<string> probes)
    {
        _localizer = localizer;
        _measurement = measurement;
        _animationsEnabled = !forceReducedMotion && new UISettings().AnimationsEnabled;
        InitializeComponent();
        ApplyMeasurementProbes(probes);

        Handle = WindowNative.GetWindowHandle(this);
        _presenter.IsResizable = false;
        _presenter.IsMaximizable = false;
        AppWindow.SetPresenter(_presenter);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, App.IconFile));
        UseFrame();
        ApplyTexts();
        _recycleBin = new ShellRecycleBin(TimeProvider.System);
        _journal = new ActionJournal(ActionJournal.DefaultPath);
        _runner = new ActionRunner(_journal, _recycleBin, TimeProvider.System);
        _memoryTimer = DispatcherQueue.CreateTimer();
        _memoryTimer.Interval = MemoryRefreshInterval;
        _memoryTimer.Tick += (_, _) => RefreshMemoryCounters();
        _updates = CreateUpdateSession();
        _updates.Changed += (_, _) =>
        {
            ShowUpdateBadge();
            ShowTrayTooltip();
        };
        _updates.InstallStarted += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        _installFollowUp = _updates.FollowUpInstall();
        ShowUpdateBadge();

        Activated += OnActivated;
        Root.KeyDown += OnRootKeyDown;
        // The XAML follows a contrast theme by itself; the two canvases are drawn again in their new colours.
        ContrastMode.Changed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            MatchVoidCanvasToBackground();
            VoidCanvas.Invalidate();
            DiskRingCanvas.Invalidate();
        });
        AppWindow.Closing += OnClosing;
    }

    internal event EventHandler? QuitRequested;

    internal event EventHandler? Hidden;

    internal nint Handle { get; }

    internal bool IsOpen => _isOpen;

    /// <summary>An update is downloading or installing; a hidden panel's process must wait for it.</summary>
    internal bool IsUpdating => _updates.IsBusy;

    /// <summary>The tray's foreground history; null when the panel runs without the tray.</summary>
    internal Func<Task<ForegroundSnapshot?>> ForegroundSource { get; set; } = () => Task.FromResult<ForegroundSnapshot?>(null);

    /// <summary>The tray's window; its program is what starts with Windows. Zero when the panel runs without the tray.</summary>
    internal nint TrayWindow { get; set; }

    /// <summary>Sets the tray icon's tooltip; does nothing when the panel runs without the tray.</summary>
    internal Action<string> TrayTooltipSink { get; set; } = _ => { };

    internal void Toggle()
    {
        if (_isOpen && !_showingWorkScreen)
        {
            Hide("toggle");
            return;
        }

        if (!_isOpen && DateTime.UtcNow - _hiddenAt < ReopenGuard)
        {
            return;
        }

        Reveal();
    }

    /// <summary>Opens the panel, or brings an open screen back in front of other windows.</summary>
    internal void Reveal()
    {
        if (!_isOpen)
        {
            Open();
            return;
        }

        Activate();
    }

    // Measurement only (--probe): leaves one graphics part out so its share of memory shows.
    // The parts are taken out before the window is first drawn, so they never create anything.
    private void ApplyMeasurementProbes(IReadOnlySet<string> probes)
    {
        if (probes.Contains("no-void"))
        {
            Root.Children.Remove(VoidCanvas);
        }

        if (probes.Contains("no-ring") && DiskRingCanvas.Parent is Panel ringHost)
        {
            ringHost.Children.Remove(DiskRingCanvas);
        }

        if (probes.Contains("no-3d"))
        {
            Cards.Transform3D = null;
        }

        if (probes.Contains("narrow"))
        {
            _panelWidth = NarrowProbeWidth;
        }

        // Shows a desktop's panel on a laptop: the cards as if there were no battery.
        _pretendNoBattery = probes.Contains("no-battery");

        // Points the Windows screen's temp list at a test folder, so a removal can be tried without the real one.
        _tempProbe = probes.FirstOrDefault(probe => probe.StartsWith(TempProbePrefix, StringComparison.Ordinal))?[TempProbePrefix.Length..];
    }

    // For MeasurementScript only: the same paths a click takes.
    internal void ShowDiskScreenForMeasurement() => ShowDiskScreen();

    internal void ShowMemoryScreenForMeasurement() => ShowMemoryScreen();

    internal void ReturnToCardsForMeasurement() => ReturnToCards();

    internal void Shutdown()
    {
        _shuttingDown = true;
        _memoryTimer.Stop();
        // Stops Win2D's game-loop thread before the window goes away.
        VoidCanvas.RemoveFromVisualTree();
        ShutdownScreens();
        Close();
    }

    // Win2D's game-loop threads and running searches stop before a screen goes away.
    private void ShutdownScreens()
    {
        _diskScreen?.Shutdown();
        _windowsScreen?.Shutdown();
        _memoryScreen?.Shutdown();
        _organizeScreen?.Shutdown();
        _duplicatesScreen?.Shutdown();
        _uninstallScreen?.Shutdown();
        _updateScreen?.Shutdown();
        _settingsScreen?.Shutdown();
    }

    // A new language shows at once (Alican, 20 October 2026), not from the next opening: the cards take
    // the new texts, and every screen made so far is dropped, to be made again in the new language when
    // it next opens. The Settings screen, where the language was chosen, is made again in its place.
    private void ChangeLanguage(LanguageChoice choice)
    {
        _localizer = new Localizer(LanguageRule.Resolve(choice, CultureInfo.CurrentUICulture.Name));
        ShutdownScreens();
        foreach (UserControl screen in LoadedScreens())
        {
            Root.Children.Remove(screen);
        }

        _diskScreen = null;
        _windowsScreen = null;
        _memoryScreen = null;
        _organizeScreen = null;
        _settingsScreen = null;
        _batteryScreen = null;
        _duplicatesScreen = null;
        _uninstallScreen = null;
        _journalScreen = null;
        _weekScreen = null;
        _updateScreen = null;

        ApplyTexts();
        ShowUpdateBadge();
        if (_diskScans.Latest is { } latest)
        {
            UpdateDiskCard(latest);
        }

        if (_memorySnapshot is { } snapshot)
        {
            UpdateMemoryCard(snapshot);
        }

        RefreshOrganizeCard();
        if (BatteryCard.Visibility == Visibility.Visible)
        {
            _ = LoadBatteryAsync();
        }

        SettingsView settings = EnsureSettingsScreen();
        settings.Refresh();
        settings.Visibility = Visibility.Visible;
        EmergeFromVoid(settings);
    }

    // One frame for the cards and every screen: no Windows title bar, and a taskbar button that
    // stays while the window is open (B-139). Vacuo's own header hides it to the tray.
    private void UseFrame()
    {
        _presenter.IsMinimizable = false;
        _presenter.IsAlwaysOnTop = true;
        _presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.IsShownInSwitchers = true;
    }

    // Only for a taller Disk legend while the cards show: the window grows, and must not flash the
    // old frame while doing so (sorunlar: winui-pencere-bicim-degisince-eski-ekran-gorunuyor).
    // Cloaked, it keeps rendering and reappears once the new size has been drawn.
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
        WindowsTitle.Text = _localizer.Get("WindowsCardTitle");
        WindowsItemTemp.Text = _localizer.Get("WindowsCardItemTemp");
        WindowsItemUpdates.Text = _localizer.Get("WindowsCardItemUpdates");
        WindowsItemComponents.Text = _localizer.Get("WindowsCardItemComponents");
        WindowsInspectButton.Content = _localizer.Get("WindowsInspectButton");
        MemoryTitle.Text = _localizer.Get("MemoryTitle");
        MemoryBody.Text = _localizer.Get("MemoryReading");
        MemoryInspectButton.Content = _localizer.Get("MemoryInspectButton");
        OrganizeTitle.Text = _localizer.Get("OrganizeTitle");
        OrganizeBody.Text = _localizer.Get("OrganizeCardReading");
        OrganizeInspectButton.Content = _localizer.Get("OrganizeInspectButton");
        BatteryTitle.Text = _localizer.Get("BatteryTitle");
        BatteryInspectButton.Content = _localizer.Get("BatteryInspectButton");
        ToolsTitle.Text = _localizer.Get("ToolsCardTitle");
        ToolsBody.Text = _localizer.Get("ToolsCardBody");
        DuplicatesButton.Content = _localizer.Get("DuplicatesFindButton");
        UninstallButton.Content = _localizer.Get("UninstallCardButton");
        QuitButton.Content = _localizer.Get("QuitButton");
        VersionText.Text = _localizer.Format("SettingsUpdatesVersionFormat", AppVersion);
        string weekLabel = _localizer.Get("WeekTitle");
        ToolTipService.SetToolTip(WeekButton, weekLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(WeekButton, weekLabel);
        string settingsLabel = _localizer.Get("SettingsTitle");
        ToolTipService.SetToolTip(SettingsButton, settingsLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SettingsButton, settingsLabel);
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

    // A screen hidden to the tray comes back as it was; otherwise the cards show. Hidden, the window
    // can move and resize unseen, so it is placed above the tray again.
    private void Open()
    {
        _measurement?.MarkPanelRequested();
        ContrastMode.Refresh();
        MatchVoidCanvasToBackground();
        MarkEvent("open");
        if (!_showingWorkScreen)
        {
            ShowCardsNow();
        }

        // A screen's lists would measure taller than the cards; the window keeps the cards' height.
        PlaceAboveTray(_panelWidth, _showingWorkScreen ? _cardPanelHeight : CardPanelHeight());
        AppWindow.Show();
        Activate();
        _isOpen = true;
        _diskScreen?.SetWindowOpen(true);
        if (_diskScans.IsStale)
        {
            RefreshDisk();
        }
        else
        {
            ShowDiskResult(_diskScans.Latest!);
        }

        RefreshMemoryCounters();
        _memoryTimer.Start();
        RefreshOrganizeCard();
        _ = LoadBatteryAsync();
        _updateScreen?.SetWindowOpen(true);
        _weekScreen?.SetWindowOpen(true);
        if (_animationsEnabled)
        {
            LogoMotion.Begin();
            LogoGlowMotion.Begin();
        }

        // A measurement run stays offline, so its numbers are the panel's own.
        if (_measurement is null)
        {
            _ = _updates.CheckAsync(byHand: false);
        }

        if (_showingWorkScreen)
        {
            FocusFirstIn(LoadedScreens().FirstOrDefault(screen => screen.Visibility == Visibility.Visible));
            return;
        }

        DiskInspectButton.Focus(FocusState.Programmatic);
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

    // The reason goes to the measurement log only, so a hide nobody asked for can be traced.
    // Hiding the window deactivates it, which would call this again; closed first, it runs once.
    private void Hide(string reason)
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;
        MarkEvent($"hidden-{reason}");
        VoidCanvas.Paused = true;
        LogoMotion.Stop();
        LogoGlowMotion.Stop();
        _diskScreen?.SetWindowOpen(false);
        _updateScreen?.SetWindowOpen(false);
        _weekScreen?.SetWindowOpen(false);
        _memoryTimer.Stop();
        // "Updated to X.Y.Z" is said for one opening only.
        if (_installFollowUp != InstallFollowUp.None)
        {
            _installFollowUp = InstallFollowUp.None;
            ShowUpdateBadge();
        }

        AppWindow.Hide();
        _hiddenAt = DateTime.UtcNow;
        _measurement?.MarkPanelHidden();
        Hidden?.Invoke(this, EventArgs.Empty);
    }

    // Back from a screen: it is drawn into the void, then the cards tip in out of it, as when the panel opens.
    private void ReturnToCards()
    {
        if (_switching)
        {
            return;
        }

        _transition++;
        MarkTransition("back-click");
        _markNextVoidFrame = _measurement is not null;
        SwitchContent(
            LoadedScreens().Where(screen => screen.Visibility == Visibility.Visible).ToArray<UIElement>(),
            () =>
            {
                ShowCardsNow();
                FitCardPanel();
                // Focus goes back to what opened the screen, so a keyboard user carries on where they were.
                (_openedFrom ?? DiskInspectButton).Focus(FocusState.Programmatic);
                _openedFrom = null;
                if (_animationsEnabled)
                {
                    // The cards were collapsed; their size is needed for the tilt's pivot.
                    Cards.UpdateLayout();
                    AnimateCardsIn();
                    EmergeFromVoid(VoidCanvas);
                }
            },
            "cards-shown");
    }

    // The cards in place at once: on opening, and as the second half of a screen change.
    private void ShowCardsNow()
    {
        _showingWorkScreen = false;
        _presenter.IsAlwaysOnTop = true;
        SettingsButton.Visibility = Visibility.Visible;
        WeekButton.Visibility = Visibility.Visible;
        foreach (UserControl screen in LoadedScreens())
        {
            screen.Visibility = Visibility.Collapsed;
        }

        VoidCanvas.Visibility = Visibility.Visible;
        GpuTemperaturePanel.Visibility = Visibility.Visible;
        CardsScroller.Visibility = Visibility.Visible;
        VoidCanvas.Paused = !_isOpen || !_animationsEnabled;
        if (_isOpen && !_animationsEnabled)
        {
            VoidCanvas.Invalidate();
        }
    }

    // The window stays as it is; the cards and the void are drawn in, then the screen grows out.
    // A screen is ordinary work: it may go behind other windows and does not hide on losing focus.
    private void ShowWorkScreen(UserControl screen, string? shownPhase = null)
    {
        if (_switching)
        {
            return;
        }

        _openedFrom = FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
        SwitchContent(
            [CardsScroller, VoidCanvas, GpuTemperaturePanel],
            () =>
            {
                _showingWorkScreen = true;
                _presenter.IsAlwaysOnTop = false;
                SettingsButton.Visibility = Visibility.Collapsed;
                WeekButton.Visibility = Visibility.Collapsed;
                VoidCanvas.Paused = true;
                VoidCanvas.Visibility = Visibility.Collapsed;
                GpuTemperaturePanel.Visibility = Visibility.Collapsed;
                CardsScroller.Visibility = Visibility.Collapsed;
                foreach (UserControl loaded in LoadedScreens())
                {
                    loaded.Visibility = loaded == screen ? Visibility.Visible : Visibility.Collapsed;
                }

                EmergeFromVoid(screen);
                FocusFirstIn(screen);
            },
            shownPhase);
    }

    // The void scene is cleared to the window's own background, the VOID colour or a contrast theme's window colour.
    private void MatchVoidCanvasToBackground()
    {
        if (Root.Background is SolidColorBrush background)
        {
            VoidCanvas.ClearColor = background.Color;
        }
    }

    private static void FocusFirstIn(UIElement? screen)
    {
        if (screen is not null && FocusManager.FindFirstFocusableElement(screen) is Control first)
        {
            first.Focus(FocusState.Programmatic);
        }
    }

    // Esc (Aşama 3f): on a screen it does what that screen's own Back does, so a screen with steps of its own
    // (Settings' info pages) goes back one step; on the cards it hides the panel. A confirm card answers Esc
    // itself first and marks it handled, so it never reaches here.
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        if (!_showingWorkScreen)
        {
            Hide("escape");
            return;
        }

        UserControl? screen = LoadedScreens().FirstOrDefault(loaded => loaded.Visibility == Visibility.Visible);
        if (screen?.FindName("BackButton") is Button { IsEnabled: true } back
            && FrameworkElementAutomationPeer.CreatePeerForElement(back)?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
        }
    }

    // What leaves shrinks and fades into the dark; once it is gone, <paramref name="swap"/> puts
    // the new content in. All on the composition thread; the window itself never changes.
    private void SwitchContent(UIElement[] leaving, Action swap, string? shownPhase)
    {
        if (!_animationsEnabled || leaving.Length == 0)
        {
            swap();
            MarkTransition(shownPhase);
            return;
        }

        _switching = true;
        Compositor compositor = ElementCompositionPreview.GetElementVisual(Root).Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f));
        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            // The left content is collapsed now; it is set back so it shows whole next time.
            foreach (UIElement element in leaving)
            {
                Visual visual = ElementCompositionPreview.GetElementVisual(element);
                visual.StopAnimation(nameof(Visual.Opacity));
                visual.StopAnimation(nameof(Visual.Scale));
                visual.Opacity = 1f;
                visual.Scale = Vector3.One;
            }

            swap();
            _switching = false;
            MarkTransition(shownPhase);
        });
        foreach (UIElement element in leaving)
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            visual.CenterPoint = new Vector3(element.ActualSize.X / 2, 0, 0);

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 0f, easing);
            fade.Duration = LeaveDuration;
            visual.StartAnimation(nameof(Visual.Opacity), fade);

            var shrink = compositor.CreateVector3KeyFrameAnimation();
            shrink.InsertKeyFrame(1f, new Vector3(LeaveScale, LeaveScale, 1f), easing);
            shrink.Duration = LeaveDuration;
            visual.StartAnimation(nameof(Visual.Scale), shrink);
        }

        batch.End();
    }

    // The new content grows out of the dark instead of appearing at once.
    private void EmergeFromVoid(UIElement element)
    {
        if (!_animationsEnabled)
        {
            return;
        }

        element.UpdateLayout();
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        Compositor compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.7f), new Vector2(0.2f, 1f));
        visual.CenterPoint = new Vector3(element.ActualSize.X / 2, 0, 0);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, easing);
        fade.Duration = ScreenFadeDuration;
        visual.StartAnimation(nameof(Visual.Opacity), fade);

        var grow = compositor.CreateVector3KeyFrameAnimation();
        grow.InsertKeyFrame(0f, new Vector3(ScreenStartScale, ScreenStartScale, 1f));
        grow.InsertKeyFrame(1f, Vector3.One, easing);
        grow.Duration = ScreenFadeDuration;
        visual.StartAnimation(nameof(Visual.Scale), grow);
    }

    // The screens are created in code the first time they open: hidden in the window from the start,
    // they held ~62 MB of the panel's memory even when never opened (B-148).
    private DiskView EnsureDiskScreen()
    {
        if (_diskScreen is not null)
        {
            return _diskScreen;
        }

        var disk = AddScreen(new DiskView());
        disk.Initialize(_localizer, _runner, _recycleBin, _animationsEnabled);
        disk.BackRequested += (_, _) => ReturnToCards();
        disk.RescanRequested += (_, _) => RefreshDisk();
        disk.JournalRequested += (_, _) => ShowJournal(disk);
        disk.Moved += (_, paths) =>
        {
            _diskScans.Forget(paths);
            UpdateDiskCard(_diskScans.Latest!);
            ShowDiskCategories();
        };
        disk.SetWindowOpen(_isOpen);
        return _diskScreen = disk;
    }

    private WindowsView EnsureWindowsScreen()
    {
        if (_windowsScreen is not null)
        {
            return _windowsScreen;
        }

        var windows = AddScreen(new WindowsView());
        windows.Initialize(
            _localizer, _runner, _recycleBin, ElevatedLauncher.NextToThisApp(),
            _tempProbe is null ? WindowsLocations.ForThisMachine() : WindowsLocations.ForThisMachine() with { UserTemp = _tempProbe },
            _animationsEnabled);
        windows.BackRequested += (_, _) => ReturnToCards();
        windows.JournalRequested += (_, _) => ShowJournal(windows);
        return _windowsScreen = windows;
    }

    private MemoryView EnsureMemoryScreen()
    {
        if (_memoryScreen is not null)
        {
            return _memoryScreen;
        }

        var memory = AddScreen(new MemoryView());
        memory.Initialize(_localizer, _animationsEnabled);
        memory.BackRequested += (_, _) => ReturnToCards();
        memory.RefreshRequested += async (_, _) => await LoadMemoryScreenAsync();
        return _memoryScreen = memory;
    }

    private OrganizeView EnsureOrganizeScreen()
    {
        if (_organizeScreen is not null)
        {
            return _organizeScreen;
        }

        var organize = AddScreen(new OrganizeView());
        organize.Initialize(_localizer, _runner, new JournalScreen(_journal, _runner), Handle, _animationsEnabled);
        organize.BackRequested += (_, _) => ReturnToCards();
        organize.JournalRequested += (_, _) => ShowJournal(organize);
        organize.Changed += (_, _) => RefreshOrganizeCard();
        return _organizeScreen = organize;
    }

    private SettingsView EnsureSettingsScreen()
    {
        if (_settingsScreen is not null)
        {
            return _settingsScreen;
        }

        var settings = AddScreen(new SettingsView());
        string? trayProgram = StartupRunKey.ProgramOfWindow(TrayWindow);
        settings.Initialize(
            _localizer,
            new SettingsStore(SettingsStore.DefaultPath),
            trayProgram is null ? null : new StartupRegistration(new StartupRunKey(), trayProgram),
            _updates);
        settings.BackRequested += (_, _) => ReturnToCards();
        settings.UpdateRequested += (_, _) => ShowUpdateScreen();
        // Not inside the picker's own event: the screen that raised it is replaced.
        settings.LanguageChanged += (_, choice) => DispatcherQueue.TryEnqueue(() => ChangeLanguage(choice));
        return _settingsScreen = settings;
    }

    private BatteryView EnsureBatteryScreen()
    {
        if (_batteryScreen is not null)
        {
            return _batteryScreen;
        }

        var battery = AddScreen(new BatteryView());
        battery.Initialize(_localizer);
        battery.BackRequested += (_, _) => ReturnToCards();
        battery.RefreshRequested += async (_, _) => await LoadBatteryAsync();
        return _batteryScreen = battery;
    }

    private DuplicatesView EnsureDuplicatesScreen()
    {
        if (_duplicatesScreen is not null)
        {
            return _duplicatesScreen;
        }

        var duplicates = AddScreen(new DuplicatesView());
        duplicates.Initialize(_localizer, _runner, _recycleBin, Handle, _animationsEnabled);
        duplicates.BackRequested += (_, _) => ReturnToCards();
        duplicates.JournalRequested += (_, _) => ShowJournal(duplicates);
        return _duplicatesScreen = duplicates;
    }

    private UninstallView EnsureUninstallScreen()
    {
        if (_uninstallScreen is not null)
        {
            return _uninstallScreen;
        }

        var uninstall = AddScreen(new UninstallView());
        uninstall.Initialize(_localizer, _runner, _recycleBin, _animationsEnabled);
        uninstall.BackRequested += (_, _) => ReturnToCards();
        uninstall.JournalRequested += (_, _) => ShowJournal(uninstall);
        return _uninstallScreen = uninstall;
    }

    private JournalView EnsureJournalScreen()
    {
        if (_journalScreen is not null)
        {
            return _journalScreen;
        }

        var journal = AddScreen(new JournalView());
        journal.Initialize(_localizer, new JournalScreen(_journal, _runner));
        journal.BackRequested += (_, _) => BackFromJournal();
        journal.Restored += (_, _) => _restoredSinceJournalOpened = true;
        return _journalScreen = journal;
    }

    private WeekView EnsureWeekScreen()
    {
        if (_weekScreen is not null)
        {
            return _weekScreen;
        }

        var week = AddScreen(new WeekView());
        week.Initialize(_localizer, _animationsEnabled);
        week.SetWindowOpen(_isOpen);
        week.BackRequested += (_, _) => ReturnToCards();
        return _weekScreen = week;
    }

    // Read fresh each time: something may have been put back from the Journal since.
    private void ShowWeekScreen()
    {
        WeekView week = EnsureWeekScreen();
        try
        {
            week.Show(WeekSummary.From(_journal.ReadAll(), DateTimeOffset.Now));
        }
        catch (JournalCorruptException exception)
        {
            week.ShowMessage(_localizer.Format("JournalReadErrorFormat", exception.Message));
        }

        ShowWorkScreen(week);
    }

    private UpdateView EnsureUpdateScreen()
    {
        if (_updateScreen is not null)
        {
            return _updateScreen;
        }

        var update = AddScreen(new UpdateView());
        update.Initialize(_localizer, _updates, _animationsEnabled);
        update.BackRequested += (_, _) => ReturnToCards();
        // The panel hides; its process waits for the download (App: IsUpdating), then the setup takes over.
        update.BackgroundRequested += (_, _) => Hide("update-background");
        update.UpdateRequested += (_, _) => _ = _updates.UpdateAsync(TrayProcessId());
        update.SetWindowOpen(_isOpen);
        return _updateScreen = update;
    }

    private void ShowUpdateScreen()
    {
        UpdateView update = EnsureUpdateScreen();
        update.Refresh();
        ShowWorkScreen(update);
    }

    // From the build (Vacuo.App.csproj <Version>), so the panel, Settings and the updater say the same.
    private static string AppVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    private static UpdateSession CreateUpdateSession()
    {
        string version = AppVersion;
        if (!ReleaseVersion.TryParse(version, out ReleaseVersion? running))
        {
            throw new InvalidOperationException($"Vacuo's own version '{version}' is not a release number.");
        }

        HttpClient http = GitHubHttp.Create(version);
        return new UpdateSession(
            new SettingsStore(SettingsStore.DefaultPath),
            new UpdateStateStore(UpdateStateStore.DefaultPath),
            new ReleaseListClient(http, ReleaseListClient.DefaultTimeout),
            new UpdateDownloader(http, UpdateDownloader.DefaultFolder, UpdateDownloader.DefaultStallTimeout),
            running!,
            UpdateDownloader.DefaultFolder);
    }

    // The setup waits for the tray as well, which holds Vacuo's files open too.
    private int? TrayProcessId() =>
        TrayWindow != 0 && GetWindowThreadProcessId(TrayWindow, out uint processId) != 0 ? (int)processId : null;

    // The header says when a new version is ready, how far its download is, or, during the first opening
    // after an update, which version this now is.
    private void ShowUpdateBadge()
    {
        string? text = _updates switch
        {
            { Phase: UpdatePhase.Downloading } => _localizer.Format("UpdateBadgeDownloadingFormat", UpdateTexts.Percent(_updates.Progress)),
            { Phase: UpdatePhase.Installing } => _localizer.Get("UpdateBadgeInstalling"),
            { Offer: { } offer } => _localizer.Format("UpdateBadgeReadyFormat", offer.Version),
            _ when _installFollowUp == InstallFollowUp.Updated => _localizer.Format("UpdateBadgeUpdatedFormat", _updates.Running),
            _ => null,
        };
        UpdateBadge.Content = text;
        UpdateBadge.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // While an update runs, the tray icon's tooltip says how far it is, for when the panel is hidden
    // (Alican, 20 October 2026). Sent only when the text changes, about once a second.
    private void ShowTrayTooltip()
    {
        string text = _updates.Phase switch
        {
            UpdatePhase.Downloading => _localizer.Format(
                "TrayTooltipUpdatingFormat", UpdateTexts.Percent(_updates.Progress), UpdateTexts.Remaining(_localizer, _updates.Remaining)),
            UpdatePhase.Installing => _localizer.Get("TrayTooltipInstalling"),
            _ => string.Empty,
        };
        if (text == _trayTooltip)
        {
            return;
        }

        _trayTooltip = text;
        TrayTooltipSink(text);
    }

    private void OnUpdateBadgeClick(object sender, RoutedEventArgs e)
    {
        if (_updates.Offer is null)
        {
            // Only "updated to X.Y.Z": a click puts it away.
            _installFollowUp = InstallFollowUp.None;
            ShowUpdateBadge();
            return;
        }

        ShowUpdateScreen();
    }

    // Where the screens sit: over the void and the cards, below the quit button.
    private T AddScreen<T>(T screen)
        where T : UserControl
    {
        Grid.SetRow(screen, 1);
        Grid.SetRowSpan(screen, 2);
        screen.Visibility = Visibility.Collapsed;
        Root.Children.Insert(Root.Children.IndexOf(QuitButton), screen);
        return screen;
    }

    // Screens not created yet are skipped.
    private UserControl[] LoadedScreens() =>
        new UserControl?[] { _diskScreen, _windowsScreen, _memoryScreen, _organizeScreen, _settingsScreen, _batteryScreen, _duplicatesScreen, _uninstallScreen, _journalScreen, _updateScreen, _weekScreen }
            .OfType<UserControl>().ToArray();

    private void ShowDiskScreen()
    {
        DiskView disk = EnsureDiskScreen();
        ShowWorkScreen(disk);
        if (_diskScans.Latest is { } latest)
        {
            disk.Show(latest);
        }
        else
        {
            disk.ShowScanning();
        }

        ShowDiskCategories();
    }

    private void ShowWindowsScreen()
    {
        WindowsView windows = EnsureWindowsScreen();
        ShowWorkScreen(windows);
        windows.Refresh();
    }

    private void ShowSettingsScreen()
    {
        SettingsView settings = EnsureSettingsScreen();
        settings.Refresh();
        ShowWorkScreen(settings);
    }

    private void ShowDuplicatesScreen()
    {
        DuplicatesView duplicates = EnsureDuplicatesScreen();
        ShowWorkScreen(duplicates);
        duplicates.Refresh();
    }

    private void ShowUninstallScreen()
    {
        UninstallView uninstall = EnsureUninstallScreen();
        ShowWorkScreen(uninstall);
        uninstall.Refresh();
    }

    private void ShowOrganizeScreen()
    {
        OrganizeView organize = EnsureOrganizeScreen();
        ShowWorkScreen(organize);
        organize.Refresh();
    }

    // Counts the Desktop under the default rule; only reads, so a failure is shown on the card.
    private async void RefreshOrganizeCard()
    {
        try
        {
            OrganizePlan plan = await OrganizeView.PlanAsync(_localizer, OrganizeView.DesktopFolder, OrganizeRule.ByType);
            OrganizeBody.Text = plan.Moves.Count == 0
                ? _localizer.Get("OrganizeCardTidy")
                : _localizer.Format("OrganizeCardFormat", plan.Moves.Count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            OrganizeBody.Text = _localizer.Format("OrganizeCardFailedFormat", exception.Message);
        }

        FitCardPanel();
    }

    // Like Memory: the screen is filled before it appears.
    private async void ShowBatteryScreen()
    {
        BatteryInspectButton.IsEnabled = false;
        BatteryView battery = EnsureBatteryScreen();
        await LoadBatteryAsync();
        BatteryInspectButton.IsEnabled = true;
        ShowWorkScreen(battery);
    }

    // The drivers answer at once, but off the UI thread all the same; only reads. Without a laptop battery
    // (a desktop, a UPS included) the card stays hidden, and the panel is laid out and measured again
    // whenever the card comes or goes. A failed reading is shown only where Windows knows of a battery.
    private async Task LoadBatteryAsync()
    {
        bool wasShown = BatteryCard.Visibility == Visibility.Visible;
        try
        {
            BatterySummary? summary = _pretendNoBattery ? null : BatterySummary.From(await Task.Run(BatteryReader.Read));
            BatteryCard.Visibility = summary is null ? Visibility.Collapsed : Visibility.Visible;
            if (summary is not null)
            {
                BatteryHealthText.Text = BatteryTexts.Health(_localizer, summary.Health);
                BatteryHealthText.Foreground = BatteryTexts.BandBrush(summary.Health.Band);
                BatteryBody.Text = BatteryTexts.CardLine(_localizer, summary);
                _batteryScreen?.Show(summary);
            }
        }
        catch (Exception exception)
        {
            string message = _localizer.Format("BatteryReadFailedFormat", exception.Message);
            BatteryCard.Visibility = BatteryReader.WindowsReportsBattery() ? Visibility.Visible : Visibility.Collapsed;
            BatteryHealthText.Text = string.Empty;
            BatteryBody.Text = message;
            _batteryScreen?.ShowMessage(message);
        }

        bool shown = BatteryCard.Visibility == Visibility.Visible;
        PlaceToolsCard(shown);
        if (wasShown != shown)
        {
            FitCardPanel();
        }
    }

    // With a battery the tools card spans both columns below them, buttons beside the text. Without one the
    // right column would end a battery card short and stretch Organize into a gap, so the tools card takes the
    // battery's place there, buttons below the text, and both columns end level (Alican, 21 October 2026).
    private void PlaceToolsCard(bool batteryShown)
    {
        // FrameworkElement.Parent is null here, so the card is looked for in the panel it would leave.
        Panel home = batteryShown ? Cards : RightColumn;
        Panel away = batteryShown ? RightColumn : Cards;
        if (!away.Children.Remove(ToolsCard))
        {
            return;
        }

        Grid.SetRow(ToolsCard, batteryShown ? 1 : BatteryCardRow);
        Grid.SetColumnSpan(ToolsCard, batteryShown ? 2 : 1);
        home.Children.Add(ToolsCard);

        Grid.SetColumn(ToolsButtons, batteryShown ? 1 : 0);
        Grid.SetRow(ToolsButtons, batteryShown ? 0 : 1);
        ToolsLayout.ColumnSpacing = batteryShown ? ToolsSideBySideSpacing : 0;
        ToolsLayout.RowSpacing = batteryShown ? 0 : ToolsStackedSpacing;
    }

    // The screen is filled before it appears, so it never opens on "Reading…".
    private async void ShowMemoryScreen()
    {
        _transition++;
        MarkTransition("memory-click");
        MemoryInspectButton.IsEnabled = false;
        MemoryView memory = EnsureMemoryScreen();
        await LoadMemoryScreenAsync();
        MemoryInspectButton.IsEnabled = true;
        ShowWorkScreen(memory, "memory-shown");
    }

    // The app list and the tray's history are read off the UI thread; only reads. The card's own
    // reading is reused while fresh: a new one waits a whole second for the paging rate.
    private async Task LoadMemoryScreenAsync()
    {
        try
        {
            Task<IReadOnlyList<RunningApp>> apps = Task.Run(RunningApps.List);
            MemorySnapshot snapshot = _memorySnapshot is { } recent && DateTime.UtcNow - _memorySnapshotAt < MemorySnapshotFreshFor
                ? recent
                : await Task.Run(() => MemoryCounters.Read(PagingWindow));
            ForegroundSnapshot? foreground = await ForegroundSource();
            ForegroundHistory? history = foreground is null ? null : ForegroundHistory.From(foreground);
            var screen = MemoryScreen.From(snapshot, await apps, history, DateTimeOffset.UtcNow);
            UpdateMemoryCard(screen.Snapshot);
            EnsureMemoryScreen().Show(screen);
        }
        catch (Exception exception)
        {
            EnsureMemoryScreen().ShowMessage(_localizer.Format("MemoryReadFailedFormat", exception.Message));
        }
    }

    // Only while the cards or the Memory screen are on screen; the Disk and Windows screens do not show memory.
    private async void RefreshMemoryCounters()
    {
        bool memoryOnScreen = !_showingWorkScreen || _memoryScreen is { Visibility: Visibility.Visible };
        if (_readingMemory || !_isOpen || !memoryOnScreen)
        {
            return;
        }

        _readingMemory = true;
        try
        {
            (MemorySnapshot snapshot, GpuTemperature? gpu) = await Task.Run(() =>
                (MemoryCounters.Read(PagingWindow), GpuTemperatureRule.Hottest(GpuTemperatureReader.Read())));
            ShowGpuTemperature(gpu);
            UpdateMemoryCard(snapshot);
            if (_memoryScreen is { Visibility: Visibility.Visible } memory)
            {
                memory.ShowSnapshot(snapshot);
            }
        }
        catch (Exception exception)
        {
            MemoryPressureText.Text = string.Empty;
            MemoryBody.Text = _localizer.Format("MemoryReadFailedFormat", exception.Message);
        }
        finally
        {
            _readingMemory = false;
        }
    }

    // Only the graphics card: the processor's temperature needs a kernel driver (Alican, 22 October 2026).
    // It shows as a sun beside the black hole, not as a line on the Memory card (Alican: "burada durmasın").
    private void ShowGpuTemperature(GpuTemperature? gpu)
    {
        _sun.Level = gpu is null ? null : HeatLevel.From(gpu.Celsius);
        GpuTemperatureValue.Visibility = gpu is null ? Visibility.Collapsed : Visibility.Visible;
        GpuTemperatureValue.Text = gpu is null ? string.Empty : _localizer.Format("GpuTemperatureValueFormat", (int)Math.Round(gpu.Celsius));
        GpuTemperatureLabel.Text = _localizer.Get(gpu is null ? "GpuTemperatureNotReported" : "GpuTemperatureLabel");
    }

    private void UpdateMemoryCard(MemorySnapshot snapshot)
    {
        _memorySnapshot = snapshot;
        _memorySnapshotAt = DateTime.UtcNow;
        MemoryPressure pressure = MemoryPressureRule.Of(snapshot);
        MemoryPressureText.Text = MemoryTexts.PressureName(_localizer, pressure);
        MemoryPressureText.Foreground = MemoryTexts.PressureBrush(pressure);
        MemoryBody.Text = MemoryTexts.Available(_localizer, snapshot);
        FitCardPanel();
    }

    // The Journal opens in the same window as the screen that asked for it; its frame and size stay.
    // The rows are read before the switch, so the Journal arrives filled, with the same animation as any screen.
    private async void ShowJournal(UserControl from)
    {
        if (_switching)
        {
            return;
        }

        _transition++;
        MarkTransition("journal-click");
        _journalOpenedFrom = from;
        _restoredSinceJournalOpened = false;
        JournalView journal = EnsureJournalScreen();
        await journal.LoadAsync();
        SwitchContent(
            [from],
            () =>
            {
                from.Visibility = Visibility.Collapsed;
                journal.Visibility = Visibility.Visible;
                EmergeFromVoid(journal);
            },
            "journal-shown");
    }

    // Put-back items are on disk again, so the screen scans before it lists anything.
    private void BackFromJournal()
    {
        if (_switching)
        {
            return;
        }

        _transition++;
        MarkTransition("journal-back-click");
        JournalView journal = EnsureJournalScreen();
        UserControl from = _journalOpenedFrom;
        SwitchContent(
            [journal],
            () =>
            {
                journal.Visibility = Visibility.Collapsed;
                from.Visibility = Visibility.Visible;
                EmergeFromVoid(from);
            },
            "journal-back-shown");
        if (!_restoredSinceJournalOpened)
        {
            return;
        }

        if (_journalOpenedFrom is WindowsView windows)
        {
            windows.Refresh();
        }
        else if (_journalOpenedFrom is OrganizeView organize)
        {
            organize.Refresh();
            RefreshOrganizeCard();
        }
        else if (_journalOpenedFrom is DuplicatesView duplicates)
        {
            duplicates.Rescan();
        }
        else if (_journalOpenedFrom is DiskView)
        {
            RefreshDisk();
        }
    }

    // The scan only reads; any failure is shown on the card instead of closing the panel.
    private async void RefreshDisk()
    {
        DiskBody.Text = _localizer.Get("DiskScanning");
        if (_showingWorkScreen && _diskScreen is { } disk)
        {
            disk.ShowScanning();
        }

        MeasureDiskCategories();
        try
        {
            ShowDiskResult(await _diskScans.ScanAsync());
        }
        catch (Exception exception)
        {
            string message = _localizer.Format("DiskScanFailedFormat", exception.Message);
            DiskBody.Text = message;
            _diskScreen?.ShowMessage(message);
        }
    }

    // Far slower than the scan, so it does not hold the scan up; the Disk screen shows it when done.
    private async void MeasureDiskCategories()
    {
        try
        {
            await _diskScans.MeasureCategoriesAsync();
            _categoriesFailure = null;
        }
        catch (Exception exception)
        {
            _categoriesFailure = _localizer.Format("DiskCategoriesFailedFormat", exception.Message);
        }

        ShowDiskCategories();
    }

    private void ShowDiskResult(ScanSummary summary)
    {
        UpdateDiskCard(summary);
        if (_showingWorkScreen && _diskScreen is { } disk)
        {
            disk.Show(summary);
        }

        ShowDiskCategories();
    }

    // Without a measurement yet the screen says it is measuring, or why it failed.
    private void ShowDiskCategories()
    {
        if (_diskScreen is not { } disk)
        {
            return;
        }

        DriveInfo drive = SystemDrive();
        DiskCategories? categories = _diskScans.Categories(drive.TotalSize - drive.AvailableFreeSpace);
        if (categories is not null)
        {
            disk.ShowCategories(DiskFlow.From(categories));
        }
        else
        {
            disk.ShowCategoriesMessage(_categoriesFailure ?? _localizer.Get("DiskCategoriesMeasuring"));
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
        FitCardPanel();
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

    // Measured, so a new card never ends up below the bottom edge.
    // On a screen too short for every card, the void above them gives up height first (vacuo-kurallar 13).
    private int CardPanelHeight()
    {
        VoidRow.Height = new GridLength(VoidHeight);
        int needed = MeasuredPanelHeight();
        int overflow = needed - AvailableLogicalHeight();
        if (overflow > 0)
        {
            VoidRow.Height = new GridLength(VoidHeight - Math.Min(overflow, VoidHeight - MinVoidHeight));
            needed = MeasuredPanelHeight();
        }

        _cardPanelHeight = Math.Max(MinPanelHeight, needed);
        return _cardPanelHeight;
    }

    private int MeasuredPanelHeight()
    {
        Root.Measure(new Size(_panelWidth, double.PositiveInfinity));
        return (int)Math.Ceiling(Root.DesiredSize.Height) + FrameAllowance + _measureShortfall;
    }

    // The measurement fell a little short of the real window (the cards scrolled by a few pixels
    // with room left on the screen, B-177). What still scrolls once laid out is learned and added,
    // so the cards fit without a scroll bar (vacuo-kurallar 13). When the panel is already as tall as the
    // screen allows, the learned pixels come out of the void above the cards instead (as in CardPanelHeight);
    // only once the void is down to its least is nothing more learned.
    private void OnCardsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // A fraction of a pixel scrolls too (0.8 at 150 %, 21 October 2026); one more covers the rounding.
        int shortBy = CardsScroller.ScrollableHeight > 0 ? (int)Math.Ceiling(CardsScroller.ScrollableHeight) + RoundingAllowance : 0;
        bool nothingLeftToGive = _cardPanelHeight >= AvailableLogicalHeight() && VoidRow.Height.Value <= MinVoidHeight;
        if (!_isOpen || _showingWorkScreen || shortBy <= 0 || _measureShortfall >= MaxMeasureShortfall || nothingLeftToGive)
        {
            return;
        }

        _measureShortfall = Math.Min(MaxMeasureShortfall, _measureShortfall + shortBy);
        FitCardPanel();
    }

    private int AvailableLogicalHeight()
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = GetDpiForWindow(Handle) / DefaultDpi;
        return (int)(workArea.Height / scale) - 2 * ScreenMargin;
    }

    // A longer legend makes the Disk card taller; the open panel grows with it.
    private void FitCardPanel()
    {
        if (!_isOpen || _showingWorkScreen)
        {
            return;
        }

        int previous = _cardPanelHeight;
        if (CardPanelHeight() != previous)
        {
            Reshape(() => PlaceAboveTray(_panelWidth, _cardPanelHeight));
        }
    }

    private void PlaceAboveTray(int logicalWidth, int logicalHeight)
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = GetDpiForWindow(Handle) / DefaultDpi;
        int width = (int)(logicalWidth * scale);
        int margin = (int)(ScreenMargin * scale);
        // On a short screen (or at a large scale) the panel never reaches above the work area.
        // Rounded up: at a fractional scale (150 %) rounding down lost part of a pixel, and the cards scrolled by it.
        int height = Math.Min((int)Math.Ceiling(logicalHeight * scale), workArea.Height - 2 * margin);
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
        UIElement[] cards = [DiskCard, WindowsCard, MemoryCard, OrganizeCard, BatteryCard, ToolsCard];

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
        _blackHole.Advance(args.Timing.ElapsedTime);
        _sun.Advance(args.Timing.ElapsedTime);
    }

    private void OnVoidDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        // In a contrast theme the scene is not drawn (Aşama 3f); the temperature stays as text.
        if (ContrastMode.IsOn)
        {
            return;
        }

        Vector2 size = sender.Size.ToVector2();
        _sun.DrawBehind(args.DrawingSession, size, _blackHole);
        _blackHole.Draw(args.DrawingSession, size);
        _sun.DrawFront(args.DrawingSession);
        if (_markNextVoidFrame)
        {
            _markNextVoidFrame = false;
            MarkTransition("void-frame");
        }

        _measurement?.RecordFrame(_blackHole.Count);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        // Only the card panel closes on losing focus; the Disk and Windows screens stay like any other window.
        MarkEvent(args.WindowActivationState.ToString());
        if (args.WindowActivationState == WindowActivationState.Deactivated && _isOpen && !_showingWorkScreen)
        {
            Hide("deactivated");
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
        Hide("closing");
    }

    private void OnDiskRingDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        DiskRing.Draw(args.DrawingSession, sender.Size.ToVector2(), _diskOverview);
    }

    private void OnDiskInspectClick(object sender, RoutedEventArgs e) => ShowDiskScreen();

    private void OnWindowsInspectClick(object sender, RoutedEventArgs e) => ShowWindowsScreen();

    private void OnMemoryInspectClick(object sender, RoutedEventArgs e) => ShowMemoryScreen();

    private void OnOrganizeInspectClick(object sender, RoutedEventArgs e) => ShowOrganizeScreen();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettingsScreen();

    private void OnWeekClick(object sender, RoutedEventArgs e) => ShowWeekScreen();

    private void OnBatteryInspectClick(object sender, RoutedEventArgs e) => ShowBatteryScreen();

    private void OnDuplicatesClick(object sender, RoutedEventArgs e) => ShowDuplicatesScreen();

    private void OnUninstallClick(object sender, RoutedEventArgs e) => ShowUninstallScreen();

    // Only with --measure: when each step of a screen change happened, numbered per change.
    // Measurement only: each event gets its own number, so repeats are all kept.
    private void MarkEvent(string name)
    {
        if (_measurement is not null)
        {
            _measurement.MarkPhase($"e{++_eventCount:000} {name}");
        }
    }

    private void MarkTransition(string? phase)
    {
        if (phase is not null)
        {
            _measurement?.MarkPhase($"{_transition:00} {phase}");
        }
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide("button");

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        QuitRequested?.Invoke(this, EventArgs.Empty);
    }
}
