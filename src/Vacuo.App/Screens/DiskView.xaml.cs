using System.Diagnostics;
using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.App.Visuals;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Scanning;
using Windows.Foundation;

namespace Vacuo.App.Screens;

/// <summary>
/// The Disk screen: buckets, rows to pick, the selection total, the confirm step and the move.
/// All decisions live in <see cref="DiskScreen"/>; this view shows them and runs the move off
/// the UI thread. Nothing moves without the user confirming, and Cancel is the default button.
/// </summary>
public sealed partial class DiskView : UserControl
{
    private static readonly TimeSpan CountUpDuration = TimeSpan.FromMilliseconds(700);
    private const int FlowParticleBudget = 600;
    private const int FlowParticleMinimum = 12;

    private readonly Dictionary<string, FrameworkElement> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CheckBox> _checks = [];
    private Localizer _localizer = null!;
    private ActionRunner _runner = null!;
    private IRecycleBin _recycleBin = null!;
    private bool _animationsEnabled;
    private DiskScreen? _screen;
    private int _skippedEntries;
    private bool _moving;
    private volatile PullBurst? _burst;
    private DiskFlow? _diskFlow;
    private volatile DiskFlowMap? _flowMap;
    private bool _windowOpen;

    public DiskView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? RescanRequested;

    internal event EventHandler? JournalRequested;

    /// <summary>Raised after a move with the paths that went to the Recycle Bin.</summary>
    internal event EventHandler<IReadOnlyList<string>>? Moved;

    internal void Initialize(Localizer localizer, ActionRunner runner, IRecycleBin recycleBin, bool animationsEnabled)
    {
        _localizer = localizer;
        _runner = runner;
        _recycleBin = recycleBin;
        _animationsEnabled = animationsEnabled;
        ScreenTitle.Text = localizer.Get("DiskTitle");
        BackButton.Content = localizer.Get("DiskBackButton");
        RescanButton.Content = localizer.Get("DiskRescanButton");
        JournalButton.Content = localizer.Get("DiskJournalButton");
        MoveButton.Content = localizer.Get("DiskMoveButton");
        MovedNote.Text = localizer.Get("DiskMovedNote");
        CategoriesTitle.Text = localizer.Get("DiskCategoriesTitle");
        Confirm.Initialize(animationsEnabled);
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => UpdateFlowRunning());
        FlowCanvas.SizeChanged += (_, _) => BuildFlow();
    }

    /// <summary>Stops Win2D's game-loop threads before the window goes away.</summary>
    internal void Shutdown()
    {
        BurstCanvas.RemoveFromVisualTree();
        FlowCanvas.RemoveFromVisualTree();
        Confirm.Shutdown();
    }

    /// <summary>The panel window was shown or hidden to the tray; the flow only moves while it is seen.</summary>
    internal void SetWindowOpen(bool open)
    {
        _windowOpen = open;
        UpdateFlowRunning();
    }

    internal void ShowCategories(DiskFlow flow)
    {
        _diskFlow = flow;
        CategoriesNote.Text = _localizer.Format("DiskCategoriesHintFormat", DiskTexts.BucketName(_localizer, Bucket.Keep));
        BuildFlow();
    }

    /// <summary>No categories to show yet: still measuring, or the measurement failed.</summary>
    internal void ShowCategoriesMessage(string message)
    {
        _diskFlow = null;
        CategoriesNote.Text = message;
        BuildFlow();
    }

    internal void ShowScanning() => ShowMessage(_localizer.Get("DiskScanning"));

    internal void ShowMessage(string message)
    {
        if (_moving)
        {
            return;
        }

        Confirm.Cancel();
        _screen = null;
        ResultPanel.Visibility = Visibility.Collapsed;
        ClearList();
        BucketList.Children.Add(BodyText(message));
        UpdateSelectionSummary();
    }

    // A scan that finishes during a move is not shown: the move updates the list itself.
    internal void Show(ScanSummary summary)
    {
        if (_moving)
        {
            return;
        }

        // A confirm step left open (e.g. the panel was hidden) belongs to the old list.
        Confirm.Cancel();
        _screen = new DiskScreen(summary);
        _skippedEntries = summary.SkippedEntries;
        ResultPanel.Visibility = Visibility.Collapsed;
        RenderList();
    }

    private void RenderList()
    {
        ClearList();
        if (_screen!.Buckets.Count == 0)
        {
            BucketList.Children.Add(BodyText(_localizer.Get("DiskNothingFound")));
        }

        foreach (DiskBucket bucket in _screen.Buckets)
        {
            BucketList.Children.Add(BucketSection(bucket));
        }

        if (_skippedEntries > 0)
        {
            BucketList.Children.Add(BodyText(_localizer.Format("DiskSkippedFormat", _skippedEntries)));
        }

        UpdateSelectionSummary();
    }

    private void ClearList()
    {
        _rows.Clear();
        _checks.Clear();
        BucketList.Children.Clear();
    }

    private StackPanel BucketSection(DiskBucket bucket)
    {
        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock
        {
            Text = $"{DiskTexts.BucketName(_localizer, bucket.Bucket)} · {DiskTexts.Size(_localizer, bucket.TotalBytes)}",
            Style = (Style)Application.Current.Resources["CardTitleStyle"],
        });
        TextBlock hint = BodyText(DiskTexts.BucketHint(_localizer, bucket.Bucket));
        hint.Margin = new Thickness(0, 0, 0, 4);
        section.Children.Add(hint);

        foreach (ScanFinding finding in bucket.Items)
        {
            section.Children.Add(Row(finding));
        }

        return section;
    }

    private Grid Row(ScanFinding finding)
    {
        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(4),
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Items that failed to move stay selected, so the user can try them again.
        var check = new CheckBox
        {
            Tag = finding.Path,
            MinWidth = 0,
            IsEnabled = _screen!.IsSelectable(finding.Path),
            IsChecked = _screen.IsSelected(finding.Path),
        };
        check.Checked += OnRowChecked;
        check.Unchecked += OnRowUnchecked;
        row.Children.Add(check);
        _checks.Add(check);

        var name = new StackPanel();
        name.Children.Add(new TextBlock
        {
            Text = DiskTexts.RuleName(_localizer, finding.Rule),
            Foreground = (Brush)Application.Current.Resources["InkBrush"],
        });
        name.Children.Add(BodyText(
            $"{DiskTexts.CostName(_localizer, finding.Rule.Cost)} · {finding.LastWriteTimeUtc.ToLocalTime().ToString("d", _localizer.Culture)} · {finding.Path}"));
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        var size = new TextBlock
        {
            Text = DiskTexts.Size(_localizer, finding.Size.Bytes),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["InkBrush"],
        };
        Grid.SetColumn(size, 2);
        row.Children.Add(size);
        _rows[finding.Path] = row;
        return row;
    }

    private static TextBlock BodyText(string text) =>
        new() { Text = text, Style = (Style)Application.Current.Resources["CardBodyStyle"] };

    private void UpdateSelectionSummary()
    {
        int count = _screen?.SelectedCount ?? 0;
        SelectionSummary.Text = count == 0
            ? _localizer.Get("DiskSelectionEmpty")
            : _localizer.Format("DiskSelectionFormat", count, DiskTexts.Size(_localizer, _screen!.SelectedBytes));
        MoveButton.IsEnabled = count > 0 && !_moving;
    }

    private void SetMoving(bool moving)
    {
        _moving = moving;
        BackButton.IsEnabled = !moving;
        RescanButton.IsEnabled = !moving;
        JournalButton.IsEnabled = !moving;
        foreach (CheckBox check in _checks)
        {
            check.IsEnabled = !moving && _screen is not null && _screen.IsSelectable((string)check.Tag);
        }

        if (moving)
        {
            MoveButton.IsEnabled = false;
            SelectionSummary.Text = _localizer.Get("DiskMoving");
        }
        else
        {
            UpdateSelectionSummary();
        }
    }

    private void OnRowChecked(object sender, RoutedEventArgs e)
    {
        _screen?.Select((string)((CheckBox)sender).Tag);
        UpdateSelectionSummary();
    }

    private void OnRowUnchecked(object sender, RoutedEventArgs e)
    {
        _screen?.Deselect((string)((CheckBox)sender).Tag);
        UpdateSelectionSummary();
    }

    private async void OnMoveClick(object sender, RoutedEventArgs e)
    {
        if (_screen is not { SelectedCount: > 0 } screen || !await ConfirmAsync(screen))
        {
            return;
        }

        IReadOnlyList<string> selected = screen.Preview().Select(finding => finding.Path).ToList();
        Dictionary<string, PulledItem> pulled = selected.ToDictionary(
            path => path, path => new PulledItem(RowCenter(_rows[path]), DiskTexts.ShortName(path)), StringComparer.OrdinalIgnoreCase);
        ResultPanel.Visibility = Visibility.Collapsed;
        SetMoving(true);
        try
        {
            MoveOutcome outcome = await Task.Run(() => screen.MoveSelected(_runner, _recycleBin));
            RenderList();
            await PlayBurstAsync(outcome.Moved
                .Select(entry => entry.Undo.OriginalPath)
                .Where(pulled.ContainsKey)
                .Select(path => pulled[path])
                .ToList());
            ShowOutcome(outcome);
        }
        catch (Exception exception)
        {
            // Unexpected (e.g. the journal could not be written): say so and show what is left.
            RenderList();
            ShowError(_localizer.Format("DiskMoveErrorFormat", exception.Message));
        }
        finally
        {
            SetMoving(false);
        }

        // Selected items no longer selected are the ones that went to the Recycle Bin.
        List<string> moved = selected.Where(path => !screen.IsSelected(path)).ToList();
        if (moved.Count > 0)
        {
            Moved?.Invoke(this, moved);
        }
    }

    private Task<bool> ConfirmAsync(DiskScreen screen)
    {
        var parts = new List<UIElement>();
        if (screen.SelectedYoursCount > 0)
        {
            parts.Add(ConfirmCard.Warning(_localizer.Format("DiskPreviewYoursWarningFormat", screen.SelectedYoursCount)));
        }

        parts.Add(ConfirmCard.List(screen.Preview().Select(finding => new ConfirmItem(
            DiskRing.ColorOf(finding.Rule.Bucket),
            DiskTexts.RuleName(_localizer, finding.Rule),
            finding.Path,
            DiskTexts.Size(_localizer, finding.Size.Bytes)))));
        parts.Add(ConfirmCard.Note(_localizer.Get("DiskPreviewRecycleNote")));
        return Confirm.AskAsync(new ConfirmRequest(
            _localizer.Get("DiskPreviewTitle"),
            _localizer.Format("DiskPreviewFormat", screen.SelectedCount, DiskTexts.Size(_localizer, screen.SelectedBytes)),
            _localizer.Get("DiskConfirmMove"),
            _localizer.Get("DiskPreviewClose"),
            parts));
    }

    private async Task PlayBurstAsync(IReadOnlyList<PulledItem> items)
    {
        if (!_animationsEnabled || items.Count == 0)
        {
            return;
        }

        var burst = new PullBurst(items, new Vector2((float)ActualWidth, (float)ActualHeight));
        _burst = burst;
        BurstCanvas.Visibility = Visibility.Visible;
        BurstCanvas.Paused = false;
        await Task.Delay(burst.Duration);
        BurstCanvas.Paused = true;
        BurstCanvas.Visibility = Visibility.Collapsed;
        _burst = null;
    }

    private void ShowOutcome(MoveOutcome outcome)
    {
        FailureList.Children.Clear();
        bool anyMoved = outcome.Moved.Count > 0;
        MovedCounter.Visibility = anyMoved ? Visibility.Visible : Visibility.Collapsed;
        MovedNote.Visibility = MovedCounter.Visibility;
        if (anyMoved)
        {
            CountUp(outcome.FreedBytes);
        }

        if (outcome.Failed.Count > 0)
        {
            TextBlock header = BodyText(_localizer.Format("DiskFailedFormat", outcome.Failed.Count));
            header.Foreground = (Brush)Application.Current.Resources["WarningBrush"];
            FailureList.Children.Add(header);
            foreach (MoveFailure failure in outcome.Failed)
            {
                FailureList.Children.Add(BodyText($"{DiskTexts.FailureReason(_localizer, failure.Reason)} · {failure.Path}"));
            }
        }

        ResultPanel.Visibility = Visibility.Visible;
    }

    private void ShowError(string message)
    {
        MovedCounter.Visibility = Visibility.Collapsed;
        MovedNote.Visibility = Visibility.Collapsed;
        FailureList.Children.Clear();
        TextBlock error = BodyText(message);
        error.Foreground = (Brush)Application.Current.Resources["WarningBrush"];
        FailureList.Children.Add(error);
        ResultPanel.Visibility = Visibility.Visible;
    }

    // The counter climbs to the moved size; with animations off it shows the number at once.
    private void CountUp(long bytes)
    {
        if (!_animationsEnabled)
        {
            MovedCounter.Text = MovedText(bytes);
            return;
        }

        var clock = Stopwatch.StartNew();
        void OnRendering(object? sender, object e)
        {
            double progress = Math.Min(1, clock.Elapsed / CountUpDuration);
            MovedCounter.Text = MovedText((long)(bytes * (1 - Math.Pow(1 - progress, 3))));
            if (progress >= 1)
            {
                CompositionTarget.Rendering -= OnRendering;
            }
        }

        MovedCounter.Text = MovedText(0);
        CompositionTarget.Rendering += OnRendering;
    }

    private string MovedText(long bytes) => _localizer.Format("DiskMovedFormat", DiskTexts.Size(_localizer, bytes));

    private Rect ListBounds() =>
        ListScroller.TransformToVisual(this).TransformBounds(new Rect(0, 0, ListScroller.ActualWidth, ListScroller.ActualHeight));

    // Rows scrolled out of sight start from the nearest edge of the list.
    private Vector2 RowCenter(FrameworkElement row)
    {
        Point center = row.TransformToVisual(this).TransformPoint(new Point(row.ActualWidth / 2, row.ActualHeight / 2));
        Rect list = ListBounds();
        return new Vector2((float)center.X, (float)Math.Clamp(center.Y, list.Top, list.Bottom));
    }

    // Rebuilt for every measurement and size; without categories only the note shows.
    private void BuildFlow()
    {
        if (_diskFlow is not { } flow || FlowCanvas.ActualWidth <= 0)
        {
            _flowMap = null;
            FlowCanvas.Visibility = _diskFlow is null ? Visibility.Collapsed : Visibility.Visible;
            UpdateFlowRunning();
            return;
        }

        double height = DiskFlowMap.HeightFor(Math.Max(flow.Categories.Count, flow.Buckets.Count));
        if (Math.Abs(FlowCanvas.Height - height) > 0.5)
        {
            // The new size comes back through SizeChanged, which builds the flow again.
            FlowCanvas.Height = height;
            return;
        }

        List<FlowChip> sources = flow.Categories
            .Select(total => new FlowChip(
                _localizer.Format(
                    "DiskCategoryChipFormat",
                    DiskTexts.CategoryName(_localizer, total.Category),
                    DiskTexts.Size(_localizer, total.Bytes),
                    DiskTexts.Percent(_localizer, total.Fraction)),
                Dot: null))
            .ToList();
        List<FlowChip> sinks = flow.Buckets
            .Select(bucket => new FlowChip(
                _localizer.Format("DiskBucketChipFormat", DiskTexts.BucketName(_localizer, bucket), DiskTexts.Size(_localizer, flow.BytesInto(bucket))),
                DiskRing.ColorOf(bucket)))
            .ToList();
        List<DiskCategory> categories = flow.Categories.Select(total => total.Category).ToList();
        List<Bucket> buckets = flow.Buckets.ToList();
        IReadOnlyList<int> particles = flow.ParticlesPerStream(FlowParticleBudget, FlowParticleMinimum);
        List<FlowStream> streams = flow.Streams
            .Select((stream, index) => new FlowStream(
                categories.IndexOf(stream.Category), buckets.IndexOf(stream.Bucket), DiskRing.ColorOf(stream.Bucket), particles[index]))
            .ToList();

        // The canvas is only drawn; a screen reader reads the same chips from its name.
        AutomationProperties.SetName(FlowCanvas, string.Join(", ", sources.Concat(sinks).Select(chip => chip.Label)));
        _flowMap = new DiskFlowMap(sources, sinks, streams, new Vector2((float)FlowCanvas.ActualWidth, (float)FlowCanvas.ActualHeight));
        FlowCanvas.Visibility = Visibility.Visible;
        UpdateFlowRunning();
    }

    // Moves only while the window and this screen are seen; with animation effects off, one still frame.
    private void UpdateFlowRunning()
    {
        bool onView = _windowOpen && Visibility == Visibility.Visible && _flowMap is not null;
        FlowCanvas.Paused = !onView || !_animationsEnabled;
        if (onView && !_animationsEnabled)
        {
            FlowCanvas.Invalidate();
        }
    }

    private void OnFlowUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args) =>
        _flowMap?.Advance(args.Timing.ElapsedTime);

    private void OnFlowDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args) =>
        _flowMap?.Draw(args.DrawingSession);

    private void OnBurstUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args) =>
        _burst?.Advance(args.Timing.ElapsedTime);

    private void OnBurstDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args) =>
        _burst?.Draw(args.DrawingSession);

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnRescanClick(object sender, RoutedEventArgs e) => RescanRequested?.Invoke(this, EventArgs.Empty);

    private void OnJournalClick(object sender, RoutedEventArgs e) => JournalRequested?.Invoke(this, EventArgs.Empty);
}
