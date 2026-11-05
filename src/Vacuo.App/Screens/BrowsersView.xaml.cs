using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Scanning;
using Vacuo.Windows;

namespace Vacuo.App.Screens;

/// <summary>
/// The Tarayıcılar screen (Aşama 3n): every browser's caches, browser by browser, moved to the Recycle Bin
/// like the Disk screen's rows. The decisions live in <see cref="BrowserCacheScreen"/>: nothing starts
/// selected, an open browser's caches are locked, and nothing moves without the confirm card.
/// </summary>
public sealed partial class BrowsersView : UserControl
{
    private readonly List<CheckBox> _checks = [];
    private Localizer _localizer = null!;
    private ActionRunner _runner = null!;
    private IRecycleBin _recycleBin = null!;
    private BrowserCacheScreen? _screen;
    private IReadOnlySet<string> _running = new HashSet<string>();
    private bool _busy;

    public BrowsersView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? JournalRequested;

    internal void Initialize(Localizer localizer, ActionRunner runner, IRecycleBin recycleBin, bool animationsEnabled)
    {
        _localizer = localizer;
        _runner = runner;
        _recycleBin = recycleBin;
        Confirm.Initialize(animationsEnabled);
        Stage.Initialize(localizer, animationsEnabled);
        MoveProgressBar.Initialize(animationsEnabled);
        ScreenTitle.Text = localizer.Get("BrowsersTitle");
        BackButton.Content = localizer.Get("DiskBackButton");
        RescanButton.Content = localizer.Get("DiskRescanButton");
        JournalButton.Content = localizer.Get("DiskJournalButton");
        Hint.Text = localizer.Get("BrowsersHint");
        MoveButton.Content = localizer.Get("DiskMoveButton");
    }

    /// <summary>Stops Win2D's game-loop thread before the window goes away.</summary>
    internal void Shutdown()
    {
        Confirm.Shutdown();
        Stage.Shutdown();
    }

    /// <summary>The panel window was shown or hidden to the tray; a move keeps going, its animation does not.</summary>
    internal void SetWindowOpen(bool open) => Stage.SetWindowOpen(open);

    /// <summary>Measures the caches and looks which browsers are open; only reads.</summary>
    internal async void Refresh()
    {
        if (_busy)
        {
            return;
        }

        Confirm.Cancel();
        _screen = null;
        ResultLines.Children.Clear();
        ClearList();
        BrowserList.Children.Add(Text(_localizer.Get("DiskScanning"), "InkSecondaryBrush"));
        UpdateSummary();
        try
        {
            (ScanSummary summary, IReadOnlySet<string> running) = await Task.Run(() =>
                (ScanSummary.Combine([new BrowserCacheScanner(KnownRoots.ForCurrentUser()).Scan()]), RunningProcessNames.Snapshot()));
            _screen = new BrowserCacheScreen(summary);
            _running = running;
            Render();
        }
        catch (Exception exception)
        {
            ClearList();
            BrowserList.Children.Add(Text(_localizer.Format("DiskScanFailedFormat", exception.Message), "WarningBrush"));
        }

        UpdateSummary();
    }

    private void ClearList()
    {
        _checks.Clear();
        BrowserList.Children.Clear();
    }

    private void Render()
    {
        ClearList();
        IReadOnlyList<BrowserCacheGroup> groups = _screen?.Groups(_running) ?? [];
        if (groups.Count == 0)
        {
            BrowserList.Children.Add(Text(_localizer.Get("BrowsersEmpty"), "InkSecondaryBrush"));
            return;
        }

        foreach (BrowserCacheGroup group in groups)
        {
            BrowserList.Children.Add(GroupPanel(group));
        }
    }

    // The browser's name and total, why it is locked when it is open, then one row per cache.
    private StackPanel GroupPanel(BrowserCacheGroup group)
    {
        string name = BrowserName(group.Browser);
        var panel = new StackPanel { Spacing = 4 };
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock title = Text(name, "InkBrush");
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        header.Children.Add(title);
        TextBlock total = Text(DiskTexts.Size(_localizer, group.TotalBytes), "InkSecondaryBrush");
        Grid.SetColumn(total, 1);
        header.Children.Add(total);
        panel.Children.Add(header);

        if (group.IsLocked)
        {
            panel.Children.Add(Text(_localizer.Format("BrowsersLockedFormat", name), "WarningBrush", fontSize: 12));
        }

        foreach (ScanFinding finding in group.Items)
        {
            panel.Children.Add(Row(finding, group.IsLocked));
        }

        return panel;
    }

    private Grid Row(ScanFinding finding, bool locked)
    {
        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(4),
            Background = Resource<Brush>("SurfaceRaisedBrush"),
            Opacity = locked ? 0.5 : 1,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string label = RowLabel(finding);
        string size = DiskTexts.Size(_localizer, finding.Size.Bytes);
        var check = new CheckBox
        {
            Tag = finding.Path,
            MinWidth = 0,
            IsChecked = _screen!.IsSelected(finding.Path),
            IsEnabled = !locked && !_busy,
        };
        AutomationProperties.SetName(check, DiskTexts.RowName(label, size));
        check.Checked += OnRowChecked;
        check.Unchecked += OnRowUnchecked;
        row.Children.Add(check);
        if (!locked)
        {
            _checks.Add(check);
        }

        TextBlock name = Text(label, "InkBrush");
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        TextBlock sizeText = Text(size, "InkBrush");
        sizeText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(sizeText, 2);
        row.Children.Add(sizeText);
        return row;
    }

    // "Default · Sayfa önbelleği": the profile's folder name, then what kind of cache it is.
    private string RowLabel(ScanFinding finding) =>
        $"{BrowserCacheScreen.ProfileOf(finding)} · {_localizer.Get("BrowserCache_" + ((BrowserCacheRule)finding.Rule).Kind)}";

    // Resource names avoid '-', so "edge-beta" becomes "Browser_edge_beta".
    private string BrowserName(BrowserDefinition browser) => _localizer.Get("Browser_" + browser.Id.Replace('-', '_'));

    private void OnRowChecked(object sender, RoutedEventArgs e)
    {
        var check = (CheckBox)sender;
        // Opened since the screen was drawn: the row cannot be chosen, and the list shows why.
        if (_screen?.Select((string)check.Tag, RunningProcessNames.Snapshot()) == false)
        {
            Refresh();
            return;
        }

        UpdateSummary();
    }

    private void OnRowUnchecked(object sender, RoutedEventArgs e)
    {
        _screen?.Deselect((string)((CheckBox)sender).Tag);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int count = _screen?.SelectedCount ?? 0;
        SelectionSummary.Text = count == 0
            ? _localizer.Get("DiskSelectionEmpty")
            : _localizer.Format("DiskSelectionFormat", count, DiskTexts.Size(_localizer, _screen!.SelectedBytes));
        MoveButton.IsEnabled = count > 0 && !_busy;
    }

    private async void OnMoveClick(object sender, RoutedEventArgs e)
    {
        if (_screen is not { SelectedCount: > 0 } screen || !await Confirm.AskAsync(MoveRequest(screen)))
        {
            return;
        }

        ResultLines.Children.Clear();
        SetBusy(true);
        SelectionSummary.Text = _localizer.Get("DiskMoving");
        MoveProgressBar.ShowFraction(0);
        bool staged = Stage.Begin(_localizer.Get("DiskMoving"));
        var progress = new Progress<MoveProgress>(report =>
        {
            MoveProgressBar.ShowFraction(report.Fraction);
            SelectionSummary.Text = RemovalTexts.Progress(_localizer, report);
            if (staged)
            {
                Stage.Report(report);
            }
        });
        try
        {
            int chosen = screen.SelectedCount;
            // Read again now: a browser opened while the card was up keeps its caches.
            IReadOnlySet<string> running = RunningProcessNames.Snapshot();
            MoveOutcome outcome = await Task.Run(() => screen.MoveSelected(running, _runner, _recycleBin, progress));
            if (staged)
            {
                await Stage.FinishAsync();
            }

            _running = running;
            Render();
            ShowOutcome(outcome, leftForOpenBrowsers: chosen - outcome.Moved.Count - outcome.Failed.Count);
        }
        catch (Exception exception)
        {
            Render();
            ResultLines.Children.Add(Text(_localizer.Format("DiskMoveErrorFormat", exception.Message), "WarningBrush"));
            Announcer.SayLines(ScreenTitle, ResultLines);
        }
        finally
        {
            Stage.Close();
            MoveProgressBar.Hide();
            SetBusy(false);
        }
    }

    private ConfirmRequest MoveRequest(BrowserCacheScreen screen) => new(
        _localizer.Get("DiskPreviewTitle"),
        _localizer.Format("DiskPreviewFormat", screen.SelectedCount, DiskTexts.Size(_localizer, screen.SelectedBytes)),
        _localizer.Get("DiskConfirmMove"),
        _localizer.Get("DiskPreviewClose"),
        [
            ConfirmCard.List(screen.Preview().Select(finding => new ConfirmItem(
                DiskRing.ColorOf(finding.Rule.Bucket),
                $"{BrowserName(((BrowserCacheRule)finding.Rule).Browser)} · {RowLabel(finding)}",
                finding.Path,
                DiskTexts.Size(_localizer, finding.Size.Bytes)))),
            ConfirmCard.Note(_localizer.Get("BrowsersPreviewNote")),
            ConfirmCard.Note(_localizer.Get("DiskPreviewRecycleNote")),
        ]);

    private void ShowOutcome(MoveOutcome outcome, int leftForOpenBrowsers)
    {
        if (outcome.Moved.Count > 0)
        {
            ResultLines.Children.Add(Text(
                _localizer.Format("DiskMovedFormat", DiskTexts.Size(_localizer, outcome.FreedBytes)), "AccentBrush"));
        }

        if (leftForOpenBrowsers > 0)
        {
            ResultLines.Children.Add(Text(_localizer.Format("BrowsersLeftOpenFormat", leftForOpenBrowsers), "WarningBrush", fontSize: 12));
        }

        if (outcome.Failed.Count > 0)
        {
            ResultLines.Children.Add(Text(_localizer.Format("DiskFailedFormat", outcome.Failed.Count), "WarningBrush", fontSize: 12));
            foreach (IGrouping<MoveFailureReason, MoveFailure> reason in outcome.Failed.GroupBy(failure => failure.Reason))
            {
                ResultLines.Children.Add(Text(
                    $"{DiskTexts.FailureReason(_localizer, reason.Key)} · {reason.Count()}", "InkSecondaryBrush", fontSize: 12));
            }
        }

        Announcer.SayLines(ScreenTitle, ResultLines);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BackButton.IsEnabled = !busy;
        RescanButton.IsEnabled = !busy;
        JournalButton.IsEnabled = !busy;
        foreach (CheckBox check in _checks)
        {
            check.IsEnabled = !busy;
        }

        UpdateSummary();
    }

    private static TextBlock Text(string text, string brushKey, double fontSize = 14) => new()
    {
        Text = text,
        FontSize = fontSize,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Resource<Brush>(brushKey),
    };

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnRescanClick(object sender, RoutedEventArgs e) => Refresh();

    private void OnJournalClick(object sender, RoutedEventArgs e) => JournalRequested?.Invoke(this, EventArgs.Empty);
}
