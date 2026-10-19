using System.Diagnostics;
using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.App.Visuals;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Scanning;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace Vacuo.App.Screens;

/// <summary>
/// The Disk screen: buckets, rows to pick, the selection total, the confirm step and the move.
/// All decisions live in <see cref="DiskScreen"/>; this view shows them and runs the move off
/// the UI thread. Nothing moves without the user confirming, and Cancel is the default button.
/// </summary>
public sealed partial class DiskView : UserControl
{
    private const int ConfirmHoleParticles = 900;
    private static readonly TimeSpan CountUpDuration = TimeSpan.FromMilliseconds(700);

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
    private readonly VoidField _confirmHole = new(ConfirmHoleParticles);
    private TaskCompletionSource<bool>? _confirmation;

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
        ConfirmTitle.Text = localizer.Get("DiskPreviewTitle");
        ConfirmMoveButton.Content = localizer.Get("DiskConfirmMove");
        ConfirmCancelButton.Content = localizer.Get("DiskPreviewClose");
    }

    /// <summary>Stops Win2D's game-loop thread before the window goes away.</summary>
    internal void Shutdown()
    {
        BurstCanvas.RemoveFromVisualTree();
        ConfirmHole.RemoveFromVisualTree();
    }

    internal void ShowScanning() => ShowMessage(_localizer.Get("DiskScanning"));

    internal void ShowMessage(string message)
    {
        if (_moving)
        {
            return;
        }

        _confirmation?.TrySetResult(false);
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
        _confirmation?.TrySetResult(false);
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

    private async Task<bool> ConfirmAsync(DiskScreen screen)
    {
        ConfirmSummary.Text = _localizer.Format(
            "DiskPreviewFormat", screen.SelectedCount, DiskTexts.Size(_localizer, screen.SelectedBytes));
        ConfirmContent.Children.Clear();
        if (screen.SelectedYoursCount > 0)
        {
            ConfirmContent.Children.Add(WarningBox(_localizer.Format("DiskPreviewYoursWarningFormat", screen.SelectedYoursCount)));
        }

        ConfirmContent.Children.Add(PreviewList(screen.Preview()));
        ConfirmContent.Children.Add(RecycleNote());

        _confirmation = new TaskCompletionSource<bool>();
        ConfirmOverlay.Visibility = Visibility.Visible;
        ConfirmHole.Paused = !_animationsEnabled;
        if (!_animationsEnabled)
        {
            ConfirmHole.Invalidate();
        }

        // Cancel has the focus, so Enter (like Esc) keeps everything where it is.
        ConfirmCancelButton.Focus(FocusState.Programmatic);
        bool confirmed = await _confirmation.Task;
        ConfirmHole.Paused = true;
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        return confirmed;
    }

    private void OnConfirmMoveClick(object sender, RoutedEventArgs e) => _confirmation?.TrySetResult(true);

    private void OnConfirmCancelClick(object sender, RoutedEventArgs e) => _confirmation?.TrySetResult(false);

    private void OnConfirmKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            _confirmation?.TrySetResult(false);
            e.Handled = true;
        }
    }

    private void OnConfirmHoleUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args) =>
        _confirmHole.Advance(args.Timing.ElapsedTime);

    private void OnConfirmHoleDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args) =>
        _confirmHole.Draw(args.DrawingSession, sender.Size.ToVector2());

    private static Border WarningBox(string text)
    {
        var warning = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        warning.Children.Add(new FontIcon { Glyph = "", FontSize = 16, Foreground = Resource<Brush>("WarningBrush") });
        warning.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440,
            Foreground = Resource<Brush>("WarningBrush"),
        });
        return new Border
        {
            Child = warning,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = Resource<Brush>("WarningBrush"),
            Background = new SolidColorBrush(Resource<Color>("WarningTintColor")),
        };
    }

    // Each item is its own rounded row with an accent edge, not a cell in a table.
    private ScrollViewer PreviewList(IReadOnlyList<ScanFinding> preview)
    {
        var rows = new StackPanel { Spacing = 6 };
        foreach (ScanFinding finding in preview)
        {
            var row = new Grid
            {
                ColumnSpacing = 12,
                Padding = new Thickness(0, 0, 14, 0),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Resource<Color>("SurfaceColor")),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new Border { Background = new SolidColorBrush(DiskRing.ColorOf(finding.Rule.Bucket)) });
            var name = new StackPanel { Padding = new Thickness(0, 10, 0, 10) };
            Grid.SetColumn(name, 1);
            name.Children.Add(new TextBlock { Text = DiskTexts.RuleName(_localizer, finding.Rule), Foreground = Resource<Brush>("InkBrush") });
            name.Children.Add(new TextBlock
            {
                Text = finding.Path,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource<Brush>("InkSecondaryBrush"),
            });
            row.Children.Add(name);
            var size = new TextBlock
            {
                Text = DiskTexts.Size(_localizer, finding.Size.Bytes),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Resource<Brush>("InkBrush"),
            };
            Grid.SetColumn(size, 2);
            row.Children.Add(size);
            rows.Children.Add(row);
        }

        return new ScrollViewer { Content = rows, MaxHeight = 240 };
    }

    private StackPanel RecycleNote()
    {
        var note = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        note.Children.Add(new FontIcon { Glyph = "", FontSize = 13, Foreground = Resource<Brush>("InkSecondaryBrush") });
        note.Children.Add(new TextBlock
        {
            Text = _localizer.Get("DiskPreviewRecycleNote"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            Foreground = Resource<Brush>("InkSecondaryBrush"),
        });
        return note;
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];

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

    private void OnBurstUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args) =>
        _burst?.Advance(args.Timing.ElapsedTime);

    private void OnBurstDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args) =>
        _burst?.Draw(args.DrawingSession);

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnRescanClick(object sender, RoutedEventArgs e) => RescanRequested?.Invoke(this, EventArgs.Empty);

    private void OnJournalClick(object sender, RoutedEventArgs e) => JournalRequested?.Invoke(this, EventArgs.Empty);
}
