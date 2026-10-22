using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Ram;
using Vacuo.Windows;
using Windows.UI;

namespace Vacuo.App.Screens;

/// <summary>
/// The Memory screen: the pressure, why Vacuo does not "clean RAM", and the running apps as idle,
/// in use, and system and protected (B-137). The user may search, pick idle and in-use apps and ask
/// them to close the way Alt+F4 would; nothing is picked beforehand and nothing is ever forced
/// (B-153, B-155). 30 s later the real difference is measured; closed apps switch off like an old
/// tube screen (B-156), and the ones still running can be brought forward to answer their question.
/// Grouping lives in <see cref="MemoryScreen"/>.
/// </summary>
public sealed partial class MemoryView : UserControl
{
    // Long enough for an app to save its state and let go of its memory.
    private static readonly TimeSpan MeasureAfterClose = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PagingWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SwitchOffDuration = TimeSpan.FromMilliseconds(450);
    // The row first folds into a thin line, then the line shrinks into a dot.
    private const float FoldedHeight = 0.04f;
    private const float FoldEnds = 0.55f;

    private readonly Dictionary<string, AppUse> _closable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Grid> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _stillRunning = new(StringComparer.OrdinalIgnoreCase);
    private Localizer _localizer = null!;
    private MemoryScreen? _screen;
    private string _filter = string.Empty;
    private bool _animationsEnabled;
    private bool _closing;

    public MemoryView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? RefreshRequested;

    internal void Initialize(Localizer localizer, bool animationsEnabled)
    {
        _localizer = localizer;
        _animationsEnabled = animationsEnabled;
        BackButton.Content = localizer.Get("DiskBackButton");
        ScreenTitle.Text = localizer.Get("MemoryTitle");
        RefreshButton.Content = localizer.Get("MemoryRefreshButton");
        SearchBox.PlaceholderText = localizer.Get("MemorySearchPlaceholder");
        WhyText.Text = localizer.Get("MemoryWhyNoCleaning");
        Confirm.Initialize(animationsEnabled);
        UpdateCloseButton();
    }

    /// <summary>Stops the confirm card's Win2D loop before the window goes away.</summary>
    internal void Shutdown() => Confirm.Shutdown();

    internal void ShowMessage(string message)
    {
        RefreshButton.IsEnabled = true;
        SectionList.Children.Clear();
        SectionList.Children.Add(Text(message, "WarningBrush"));
    }

    // A fresh list starts with nothing picked; the search stays.
    internal void Show(MemoryScreen screen)
    {
        _screen = screen;
        RefreshButton.IsEnabled = true;
        ShowSnapshot(screen.Snapshot);
        HistoryText.Text = screen.WatchingSince is { } since
            ? _localizer.Format("MemoryWatchingSinceFormat", since.ToLocalTime().ToString("t", _localizer.Culture))
            : _localizer.Get("MemoryNoHistory");
        _selected.Clear();
        RenderList();
    }

    /// <summary>A newer reading of the counters; the app list stays until the next refresh.</summary>
    internal void ShowSnapshot(MemorySnapshot snapshot)
    {
        MemoryPressure pressure = MemoryPressureRule.Of(snapshot);
        PressureText.Text = MemoryTexts.PressureName(_localizer, pressure);
        PressureText.Foreground = MemoryTexts.PressureBrush(pressure);
        PressureHint.Text = MemoryTexts.PressureHint(_localizer, pressure);
        NumbersText.Text = $"{MemoryTexts.Available(_localizer, snapshot)} · {MemoryTexts.Committed(_localizer, snapshot)}";
    }

    // Picks survive a search, so an app found by name can be added to the ones already picked.
    private void RenderList()
    {
        _closable.Clear();
        _rows.Clear();
        SectionList.Children.Clear();
        if (_screen is null)
        {
            return;
        }

        foreach (MemorySection section in _screen.Sections)
        {
            AddSection(section, _screen.HistoryKnown);
        }

        if (_filter.Length > 0 && _rows.Count == 0)
        {
            SectionList.Children.Add(Text(_localizer.Format("MemoryNoMatchFormat", _filter), "InkSecondaryBrush"));
        }

        UpdateCloseButton();
    }

    private void AddSection(MemorySection section, bool historyKnown)
    {
        List<AppUse> shown = section.Shown
            .Where(use => _filter.Length == 0 || use.App.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (_filter.Length > 0 && shown.Count == 0)
        {
            return;
        }

        var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
        header.Children.Add(new TextBlock
        {
            Text = $"{MemoryTexts.GroupName(_localizer, section.Group)} · {DiskTexts.Size(_localizer, section.TotalBytes)}",
            Style = Resource<Style>("CardTitleStyle"),
        });
        header.Children.Add(Text(MemoryTexts.GroupHint(_localizer, section.Group), "InkSecondaryBrush", fontSize: 12));
        SectionList.Children.Add(header);

        if (shown.Count == 0 && section.OthersCount == 0)
        {
            SectionList.Children.Add(Text(_localizer.Get("MemoryEmptyGroup"), "InkSecondaryBrush", fontSize: 12));
            return;
        }

        foreach (AppUse use in shown)
        {
            SectionList.Children.Add(Row(use, historyKnown));
        }

        // Small apps are summed, so a search cannot find them one by one.
        if (_filter.Length == 0 && section.OthersCount > 0)
        {
            SectionList.Children.Add(Text(
                _localizer.Format("MemoryOthersFormat", section.OthersCount, DiskTexts.Size(_localizer, section.OthersBytes)),
                "InkSecondaryBrush",
                fontSize: 12));
        }
    }

    // Same rounded row as the Journal; the edge is lit for idle apps, amber for one still running after
    // a close request. Protected apps get no box at all, so they cannot be picked.
    private Grid Row(AppUse use, bool historyKnown)
    {
        string name = use.App.Name;
        string edge = _stillRunning.Contains(name) ? "WarningBrush" : use.Group == AppGroup.Idle ? "AccentBrush" : "LineBrush";
        var row = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(0, 0, 14, 0),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Resource<Color>("SurfaceColor")),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new Border { Background = Resource<Brush>(edge) });

        if (MemoryScreen.CanClose(use))
        {
            _closable[name] = use;
            var pick = new CheckBox { MinWidth = 0, VerticalAlignment = VerticalAlignment.Center, IsChecked = _selected.Contains(name) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pick, name);
            pick.Checked += (_, _) => Pick(name, picked: true);
            pick.Unchecked += (_, _) => Pick(name, picked: false);
            Grid.SetColumn(pick, 1);
            row.Children.Add(pick);
        }

        var details = new StackPanel { Padding = new Thickness(0, 8, 0, 8), Spacing = 2 };
        details.Children.Add(Text(name, "InkBrush"));
        string usage = MemoryTexts.Usage(_localizer, use, historyKnown);
        if (usage.Length > 0)
        {
            details.Children.Add(Text(usage, "InkSecondaryBrush", fontSize: 12));
        }

        Grid.SetColumn(details, 2);
        row.Children.Add(details);

        TextBlock size = Text(DiskTexts.Size(_localizer, use.App.PrivateBytes), "InkBrush");
        size.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(size, 3);
        row.Children.Add(size);
        _rows[name] = row;
        return row;
    }

    private void Pick(string name, bool picked)
    {
        if (picked)
        {
            _selected.Add(name);
        }
        else
        {
            _selected.Remove(name);
        }

        UpdateCloseButton();
    }

    private void UpdateCloseButton()
    {
        CloseButton.Content = _selected.Count == 0
            ? _localizer.Get("MemoryCloseButton")
            : _localizer.Format("MemoryCloseButtonFormat", _selected.Count);
        CloseButton.IsEnabled = _selected.Count > 0 && !_closing;
    }

    private async void OnCloseClick(object sender, RoutedEventArgs e)
    {
        List<AppUse> picked = _selected
            .Where(_closable.ContainsKey)
            .Select(name => _closable[name])
            .OrderByDescending(use => use.App.PrivateBytes)
            .ToList();
        if (picked.Count == 0 || !await ConfirmAsync(picked))
        {
            return;
        }

        _closing = true;
        UpdateCloseButton();
        RefreshButton.IsEnabled = false;
        try
        {
            ShowOutcome([Text(_localizer.Format("MemoryClosingFormat", picked.Count), "InkSecondaryBrush")]);
            List<string> names = picked.Select(use => use.App.Name).ToList();
            MemorySnapshot before = await Task.Run(() => MemoryCounters.Read(PagingWindow));
            foreach (string name in names)
            {
                AppCloser.RequestClose(name);
            }

            await Task.Delay(MeasureAfterClose);
            MemorySnapshot after = await Task.Run(() => MemoryCounters.Read(PagingWindow));
            IReadOnlyList<RunningApp> running = await Task.Run(RunningApps.List);
            CloseOutcome outcome = CloseOutcome.From(names, running, before, after);
            _stillRunning.Clear();
            _stillRunning.UnionWith(outcome.StillRunning);
            ShowOutcome(OutcomeLines(outcome));
            await SwitchOffAsync(outcome.Closed);
        }
        catch (Exception exception)
        {
            // Unexpected (e.g. a counter could not be read): shown here, never a crash.
            ShowOutcome([Text(_localizer.Format("MemoryCloseFailedFormat", exception.Message), "WarningBrush")]);
        }
        finally
        {
            _closing = false;
            RefreshButton.IsEnabled = true;
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private Task<bool> ConfirmAsync(IReadOnlyList<AppUse> picked)
    {
        long total = picked.Sum(use => use.App.PrivateBytes);
        var items = picked.Select(use => new ConfirmItem(
            Resource<Color>(use.Group == AppGroup.Idle ? "AccentColor" : "LineColor"),
            use.App.Name,
            MemoryTexts.GroupName(_localizer, use.Group),
            DiskTexts.Size(_localizer, use.App.PrivateBytes)));
        return Confirm.AskAsync(new ConfirmRequest(
            _localizer.Get("MemoryConfirmTitle"),
            _localizer.Format("MemoryConfirmSummaryFormat", picked.Count, DiskTexts.Size(_localizer, total)),
            _localizer.Get("MemoryConfirmClose"),
            _localizer.Get("DiskPreviewClose"),
            [ConfirmCard.List(items), ConfirmCard.Note(_localizer.Get("MemoryConfirmNote"))]));
    }

    private List<UIElement> OutcomeLines(CloseOutcome outcome)
    {
        var lines = new List<UIElement>();
        if (outcome.Closed.Count > 0)
        {
            lines.Add(Text(_localizer.Format("MemoryClosedFormat", string.Join(", ", outcome.Closed)), "InkBrush"));
        }

        if (outcome.StillRunning.Count > 0)
        {
            lines.Add(Text(_localizer.Get("MemoryStillRunningHeader"), "WarningBrush"));
            foreach (string name in outcome.StillRunning)
            {
                lines.Add(StillRunningLine(name));
            }
        }

        lines.Add(Text(
            _localizer.Format(
                "MemoryDifferenceFormat",
                MemoryTexts.SignedSize(_localizer, outcome.AvailableGainedBytes),
                MemoryTexts.SignedSize(_localizer, -outcome.CommitDroppedBytes)),
            "InkSecondaryBrush",
            fontSize: 12));
        return lines;
    }

    // The app answers its own question; Vacuo only brings it forward (B-155).
    private StackPanel StillRunningLine(string name)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        TextBlock label = Text(name, "InkBrush");
        label.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(label);
        var switchTo = new Button
        {
            Content = _localizer.Get("MemorySwitchToButton"),
            Style = Resource<Style>("VoidGhostButtonStyle"),
            Padding = new Thickness(14, 4, 14, 4),
        };
        switchTo.Click += (_, _) => AppCloser.BringToFront(name);
        line.Children.Add(switchTo);
        return line;
    }

    // Each closed app's row switches off like an old tube screen: it folds into a lit line, then the
    // line shrinks into a dot and goes dark (B-156). The rows then leave with the refreshed list.
    private Task SwitchOffAsync(IReadOnlyList<string> closed)
    {
        List<Grid> rows = closed.Where(_rows.ContainsKey).Select(name => _rows[name]).ToList();
        if (!_animationsEnabled || rows.Count == 0)
        {
            return Task.CompletedTask;
        }

        Compositor compositor = ElementCompositionPreview.GetElementVisual(SectionList).Compositor;
        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var done = new TaskCompletionSource();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() => done.TrySetResult());
        foreach (Grid row in rows)
        {
            SwitchOff(row, compositor);
        }

        batch.End();
        return done.Task;
    }

    private static void SwitchOff(Grid row, Compositor compositor)
    {
        // A lit layer over the row, so the folding line glows in the accent colour.
        var glow = new Border { Background = Resource<Brush>("AccentBrush"), CornerRadius = row.CornerRadius, Opacity = 0 };
        Grid.SetColumnSpan(glow, row.ColumnDefinitions.Count);
        row.Children.Add(glow);
        row.UpdateLayout();

        Visual glowVisual = ElementCompositionPreview.GetElementVisual(glow);
        var light = compositor.CreateScalarKeyFrameAnimation();
        light.InsertKeyFrame(0f, 0f);
        light.InsertKeyFrame(FoldEnds, 0.9f);
        light.InsertKeyFrame(1f, 0.9f);
        light.Duration = SwitchOffDuration;
        glowVisual.StartAnimation(nameof(Visual.Opacity), light);

        Visual visual = ElementCompositionPreview.GetElementVisual(row);
        visual.CenterPoint = new Vector3(row.ActualSize / 2, 0);
        var fold = compositor.CreateVector3KeyFrameAnimation();
        fold.InsertKeyFrame(0f, Vector3.One);
        fold.InsertKeyFrame(FoldEnds, new Vector3(1f, FoldedHeight, 1f));
        fold.InsertKeyFrame(1f, new Vector3(0f, FoldedHeight, 1f));
        fold.Duration = SwitchOffDuration;
        visual.StartAnimation(nameof(Visual.Scale), fold);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 1f);
        fade.InsertKeyFrame(FoldEnds, 1f);
        fade.InsertKeyFrame(1f, 0f);
        fade.Duration = SwitchOffDuration;
        visual.StartAnimation(nameof(Visual.Opacity), fade);
    }

    private void ShowOutcome(IEnumerable<UIElement> lines)
    {
        OutcomePanel.Children.Clear();
        foreach (UIElement line in lines)
        {
            OutcomePanel.Children.Add(line);
        }

        OutcomeCard.Visibility = Visibility.Visible;
        Announcer.SayLines(ScreenTitle, OutcomePanel);
    }

    private static TextBlock Text(string text, string brushKey, double fontSize = 14) => new()
    {
        Text = text,
        FontSize = fontSize,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Resource<Brush>(brushKey),
    };

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _filter = SearchBox.Text.Trim();
        RenderList();
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        Confirm.Cancel();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
}
