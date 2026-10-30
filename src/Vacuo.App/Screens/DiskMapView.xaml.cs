using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.App.Visuals;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.DiskMap;
using Vacuo.Core.Notices;
using Vacuo.Core.Scanning;
using Windows.Foundation;
using Windows.UI;

namespace Vacuo.App.Screens;

/// <summary>
/// The disk map (Aşama 3k, draft B): the whole drive as boxes, largest first. All decisions live in
/// <see cref="DiskMapScreen"/> and <see cref="DiskMapLayout"/>; this view draws them. The list shows the same
/// items, so a keyboard or a screen reader reaches everything, however small its box. The boxes are plain
/// elements: nothing draws while the screen just sits there.
/// </summary>
public sealed partial class DiskMapView : UserControl
{
    private const float DiveFromScale = 0.4f;
    private const float RiseFromScale = 0.85f;
    private const double SmallestDrawnBox = 2;
    private const double SmallestNamedWidth = 52;
    private const double SmallestNamedHeight = 26;
    private const double SmallestSizedHeight = 44;
    private const double SmallestMarkedBox = 34;
    private const int ChipsShown = 1;

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DiveDuration = TimeSpan.FromMilliseconds(240);
    private static readonly Color Ink = Color.FromArgb(0xFF, 0xEC, 0xE7, 0xF5);
    private static readonly Color InkSecondary = Color.FromArgb(0xFF, 0xAB, 0xA1, 0xBF);
    private static readonly Color Void = Color.FromArgb(0xFF, 0x0A, 0x08, 0x11);
    private static readonly Color Quiet = Color.FromArgb(0xFF, 0x1D, 0x17, 0x2A);

    // From "takes little" to "takes a lot". The largest boxes fill most of the map, so even the top stays muted.
    private static readonly Color[] HeatColors =
    [
        Color.FromArgb(0xFF, 0x24, 0x1C, 0x38),
        Color.FromArgb(0xFF, 0x2A, 0x21, 0x40),
        Color.FromArgb(0xFF, 0x35, 0x2A, 0x50),
        Color.FromArgb(0xFF, 0x41, 0x34, 0x63),
        Color.FromArgb(0xFF, 0x4E, 0x3E, 0x78),
        Color.FromArgb(0xFF, 0x5C, 0x4A, 0x8E),
    ];

    private readonly MapProtection _protection = new(MapProtectedPaths.ForThisMachine());
    private readonly Dictionary<FolderNode, FrameworkElement> _drawn = [];
    private Localizer _localizer = null!;
    private ActionRunner _runner = null!;
    private IRecycleBin _recycleBin = null!;
    private bool _animationsEnabled;
    private bool _moving;
    private volatile PullBurst? _burst;
    private DiskMapScreen? _screen;
    private long _usedBytes;
    private DateTimeOffset _scannedAt;
    private CancellationTokenSource? _scan;
    private bool _showingList;
    private FolderNode? _focusCheckOf;
    private bool _focusFirstBox;

    public DiskMapView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? JournalRequested;

    /// <summary>Raised after a move sent something to the Recycle Bin.</summary>
    internal event EventHandler? Moved;

    internal void Initialize(Localizer localizer, ActionRunner runner, IRecycleBin recycleBin, bool animationsEnabled)
    {
        _localizer = localizer;
        _runner = runner;
        _recycleBin = recycleBin;
        _animationsEnabled = animationsEnabled;
        ScreenTitle.Text = localizer.Get("DiskMapTitle");
        BackButton.Content = localizer.Get("DiskBackButton");
        RescanButton.Content = localizer.Get("DiskRescanButton");
        JournalButton.Content = localizer.Get("DiskJournalButton");
        MoveButton.Content = localizer.Get("DiskMoveButton");
        StopButton.Content = localizer.Get("DiskMapStop");
        RampLess.Text = localizer.Get("DiskMapRampLess");
        RampMore.Text = localizer.Get("DiskMapRampMore");
        MapPill.Content = PillContent("\uE80A", localizer.Get("DiskMapPillMap"));
        ListPill.Content = PillContent("\uE8FD", localizer.Get("DiskMapPillList"));
        AutomationProperties.SetName(MapPill, localizer.Get("DiskMapPillMap"));
        AutomationProperties.SetName(ListPill, localizer.Get("DiskMapPillList"));
        Confirm.Initialize(animationsEnabled);
        ShowPills();
    }

    /// <summary>A scan does not outlive the window; Win2D's game-loop threads stop before it goes away.</summary>
    internal void Shutdown()
    {
        _scan?.Cancel();
        BurstCanvas.RemoveFromVisualTree();
        Confirm.Shutdown();
    }

    /// <summary>Something came back from the Recycle Bin: the next look scans again.</summary>
    internal void Forget()
    {
        if (!_moving)
        {
            _screen = null;
        }
    }

    /// <summary>Shows the last scan while it is fresh, or scans.</summary>
    internal void Refresh()
    {
        // The tree is being changed on another thread; the screen comes back when the move ends.
        if (_moving)
        {
            return;
        }

        // A confirm step left open (e.g. the panel was hidden) belongs to what was shown before.
        Confirm.Cancel();
        if (_screen is not null && DateTimeOffset.UtcNow - _scannedAt < MaxAge)
        {
            Render();
        }
        else
        {
            StartScan();
        }
    }

    // Only reads. A new scan replaces one still running.
    private async void StartScan()
    {
        if (SystemDrive.Read() is not { } drive)
        {
            SetNote(_localizer.Get("DiskMapNoDrive"), warning: true);
            return;
        }

        _scan?.Cancel();
        var scan = new CancellationTokenSource();
        _scan = scan;
        string root = drive.RootDirectory.FullName;
        long used = drive.TotalSize - drive.AvailableFreeSpace;
        var progress = new Progress<double>(share =>
        {
            if (_scan == scan)
            {
                ScanPercent.Text = share.ToString("P0", _localizer.Culture);
            }
        });

        ResultPanel.Visibility = Visibility.Collapsed;
        MoveButton.IsEnabled = false;
        ShowProgress(_localizer.Get("DiskMapScanning"), canStop: true);
        SetNote(string.Empty, warning: false);
        try
        {
            FolderTreeScan result = await Task.Run(() => new FolderTreeScanner().Scan(root, used, progress, scan.Token));
            _screen = new DiskMapScreen(result, _protection);
            _usedBytes = used;
            _scannedAt = DateTimeOffset.UtcNow;
            SetNote(result.SkippedFolders > 0 ? _localizer.Format("DiskMapSkippedFormat", result.SkippedFolders) : _localizer.Get("DiskMapHint"), warning: false);
        }
        catch (OperationCanceledException)
        {
            if (_scan == scan)
            {
                SetNote(_localizer.Get("DiskMapStopped"), warning: false);
            }
        }
        catch (Exception exception)
        {
            SetNote(_localizer.Format("DiskMapFailedFormat", exception.Message), warning: true);
        }
        finally
        {
            if (_scan == scan)
            {
                _scan = null;
                ScanPanel.Visibility = Visibility.Collapsed;
                Render();
            }

            scan.Dispose();
        }
    }

    // Hides the map and the list: nothing on them can be used while a scan or a move runs.
    private void ShowProgress(string note, bool canStop)
    {
        ScanPercent.Text = 0d.ToString("P0", _localizer.Culture);
        ScanNote.Text = note;
        StopButton.Visibility = canStop ? Visibility.Visible : Visibility.Collapsed;
        ScanPanel.Visibility = Visibility.Visible;
        MapCanvas.Visibility = Visibility.Collapsed;
        ListScroller.Visibility = Visibility.Collapsed;
    }

    private void Render()
    {
        ShowPills();
        _drawn.Clear();
        if (_screen is not { } screen)
        {
            UsedText.Text = string.Empty;
            TrailPanel.Children.Clear();
            MapCanvas.Children.Clear();
            ListPanel.Children.Clear();
            ChipPanel.Children.Clear();
            SelectionSummary.Text = string.Empty;
            MoveButton.IsEnabled = false;
            return;
        }

        UsedText.Text = _localizer.Format("DiskMapUsedFormat", DiskTexts.Size(_localizer, _usedBytes));
        RenderTrail(screen);
        MapCanvas.Visibility = _showingList ? Visibility.Collapsed : Visibility.Visible;
        ListScroller.Visibility = _showingList ? Visibility.Visible : Visibility.Collapsed;
        if (_showingList)
        {
            RenderList(screen);
        }
        else
        {
            RenderMap(screen);
        }

        RenderSelection(screen);
    }

    private void RenderTrail(DiskMapScreen screen)
    {
        TrailPanel.Children.Clear();
        foreach (FolderNode step in screen.Trail)
        {
            bool last = ReferenceEquals(step, screen.Current);
            if (!ReferenceEquals(step, screen.Root))
            {
                TrailPanel.Children.Add(new FontIcon { Glyph = "\uE76C", FontSize = 10, Foreground = Themed("AccentBrush") });
            }

            var crumb = new Button
            {
                Style = (Style)Resources["MapBoxStyle"],
                Padding = new Thickness(6, 4, 6, 4),
                Foreground = Themed(last ? "InkBrush" : "InkSecondaryBrush"),
                Content = new TextBlock { Text = NameOf(step), FontSize = 13, FontWeight = last ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal },
            };
            crumb.Click += (_, _) =>
            {
                screen.GoTo(step);
                PlayMove(RiseFromScale, new Vector2((float)MapFrame.ActualWidth / 2, (float)MapFrame.ActualHeight / 2));
                Render();
            };
            TrailPanel.Children.Add(crumb);
        }
    }

    private void RenderMap(DiskMapScreen screen)
    {
        _drawn.Clear();
        MapCanvas.Children.Clear();
        if (MapFrame.ActualWidth <= 0 || MapFrame.ActualHeight <= 0)
        {
            return;
        }

        IReadOnlyList<MapBox> boxes = screen.Boxes(MapFrame.ActualWidth, MapFrame.ActualHeight);
        if (boxes.Count == 0)
        {
            SetNote(_localizer.Get("DiskMapEmpty"), warning: false);
        }

        foreach (MapBox box in boxes.Where(IsDrawable))
        {
            MapCanvas.Children.Add(box.Inner.Count > 0 ? Nested(screen, box) : Flat(screen, box));
        }
    }

    private static bool IsDrawable(MapBox box) => box.Rect.Width >= SmallestDrawnBox && box.Rect.Height >= SmallestDrawnBox;

    // A large folder: its header opens it, and what is inside it can be used straight away.
    private Grid Nested(DiskMapScreen screen, MapBox box)
    {
        Grid outer = Placed(box);
        outer.Background = Themed("SurfaceRaisedBrush");
        outer.BorderBrush = Themed("LineBrush");
        outer.BorderThickness = new Thickness(1);
        outer.CornerRadius = new CornerRadius(4);

        var header = new Button
        {
            Style = (Style)Resources["MapBoxStyle"],
            Height = DiskMapLayout.HeaderHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(7, 0, 7, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = HeaderContent(box),
        };
        AutomationProperties.SetName(header, _localizer.Format("DiskMapEnterFormat", NameOf(box.Item), DiskTexts.Size(_localizer, box.Item.Bytes)));
        header.Click += (sender, _) => Open(screen, box, (Button)sender);
        Describe(header, screen, box);
        Remember(box.Item, header);
        outer.Children.Add(header);

        var inner = new Canvas();
        foreach (MapBox kid in box.Inner.Where(IsDrawable))
        {
            inner.Children.Add(Flat(screen, kid));
        }

        outer.Children.Add(inner);
        return outer;
    }

    private Grid Flat(DiskMapScreen screen, MapBox box)
    {
        Grid tile = Placed(box);
        (Brush fill, Color text) = LooksOf(box);
        var content = new StackPanel { Spacing = 1 };
        bool named = tile.Width >= SmallestNamedWidth && tile.Height >= SmallestNamedHeight;
        if (named)
        {
            content.Children.Add(Label(box, NameOf(box.Item), text, 12, Microsoft.UI.Text.FontWeights.SemiBold));
        }

        if (named && tile.Height >= SmallestSizedHeight)
        {
            content.Children.Add(Label(box, DiskTexts.Size(_localizer, box.Item.Bytes), text, 12, Microsoft.UI.Text.FontWeights.Normal));
        }

        var button = new Button { Style = (Style)Resources["MapBoxStyle"], Background = fill, Content = content };
        string size = DiskTexts.Size(_localizer, box.Item.Bytes);
        AutomationProperties.SetName(button, box.Item.Node is { } node && screen.CanEnter(node)
            ? _localizer.Format("DiskMapEnterFormat", NameOf(box.Item), size)
            : _localizer.Format("DiskMapItemFormat", NameOf(box.Item), size));
        button.Click += (sender, _) => Open(screen, box, (Button)sender);
        Describe(button, screen, box);
        Remember(box.Item, button);
        tile.Children.Add(button);

        if (box.Item.Node is { } item && tile.Width >= SmallestMarkedBox && tile.Height >= SmallestMarkedBox)
        {
            AddMark(tile, screen, item, text);
        }

        return tile;
    }

    // A move starts its burst from where the item is drawn.
    private void Remember(MapItem item, FrameworkElement element)
    {
        if (item.Node is { } node)
        {
            _drawn[node] = element;
        }
    }

    private static Grid Placed(MapBox box)
    {
        var tile = new Grid { Width = Math.Max(0, box.Rect.Width - 2), Height = Math.Max(0, box.Rect.Height - 2) };
        Canvas.SetLeft(tile, box.Rect.X + 1);
        Canvas.SetTop(tile, box.Rect.Y + 1);
        return tile;
    }

    // Colour tells the size; a contrast theme keeps the theme's colours, with the size in the words.
    private static (Brush Fill, Color Text) LooksOf(MapBox box)
    {
        if (ContrastMode.IsOn)
        {
            return (Themed("SurfaceRaisedBrush"), Ink);
        }

        if (box.IsCore)
        {
            return (CoreBrush(), Ink);
        }

        if (box.Item.Kind is MapItemKind.Rest or MapItemKind.Unread)
        {
            return (new SolidColorBrush(Quiet), InkSecondary);
        }

        return (new SolidColorBrush(HeatColors[box.Heat]), Ink);
    }

    // What takes the most space is drawn as the black hole that pulls the disk in.
    private static RadialGradientBrush CoreBrush()
    {
        var brush = new RadialGradientBrush { Center = new Point(0.5, 0.54), GradientOrigin = new Point(0.5, 0.54), RadiusX = 0.72, RadiusY = 0.72 };
        brush.GradientStops.Add(new GradientStop { Color = Void, Offset = 0.14 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xFF, 0x16, 0x11, 0x24), Offset = 0.3 });
        brush.GradientStops.Add(new GradientStop { Color = HeatColors[1], Offset = 0.6 });
        brush.GradientStops.Add(new GradientStop { Color = HeatColors[3], Offset = 1 });
        return brush;
    }

    // On the black hole the words sit on a dark plate: its colours run from black to bright under them.
    private static FrameworkElement Label(MapBox box, string text, Color color, double size, global::Windows.UI.Text.FontWeight weight)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = weight, Foreground = new SolidColorBrush(color), TextTrimming = TextTrimming.CharacterEllipsis };
        return box.IsCore && !ContrastMode.IsOn
            ? new Border { Child = label, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(5, 2, 5, 2), CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0xB8, Void.R, Void.G, Void.B)) }
            : label;
    }

    private Grid HeaderContent(MapBox box)
    {
        var line = new Grid { ColumnSpacing = 8 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(new TextBlock { Text = NameOf(box.Item), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Themed("InkBrush") });
        var size = new TextBlock { Text = DiskTexts.Size(_localizer, box.Item.Bytes), FontSize = 12, Foreground = Themed("InkSecondaryBrush") };
        Grid.SetColumn(size, 1);
        line.Children.Add(size);
        return line;
    }

    // A check box where the node can move, a lock where it cannot.
    private void AddMark(Grid tile, DiskMapScreen screen, FolderNode node, Color text)
    {
        if (screen.LockOf(node) != MapLock.None)
        {
            tile.Children.Add(new FontIcon
            {
                Glyph = "\uE72E",
                FontSize = 13,
                Opacity = 0.75,
                Margin = new Thickness(0, 0, 6, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Foreground = new SolidColorBrush(text),
            });
            return;
        }

        CheckBox check = SelectBox(screen, node);
        check.HorizontalAlignment = HorizontalAlignment.Right;
        check.VerticalAlignment = VerticalAlignment.Bottom;
        check.Margin = new Thickness(0, 0, 4, 4);
        tile.Children.Add(check);
    }

    private CheckBox SelectBox(DiskMapScreen screen, FolderNode node)
    {
        bool selected = screen.IsSelected(node);
        var check = new CheckBox
        {
            MinWidth = 0,
            IsChecked = selected,
            // Inside a selected folder it goes along with the folder and cannot be taken out alone.
            IsEnabled = !selected || screen.Preview().Contains(node),
        };
        AutomationProperties.SetName(check, _localizer.Format("DiskMapSelectFormat", node.Name));
        check.Click += (_, _) =>
        {
            if (check.IsChecked == true)
            {
                screen.TrySelect(node);
            }
            else
            {
                screen.Unselect(node);
            }

            _focusCheckOf = node;
            Render();
        };
        if (ReferenceEquals(_focusCheckOf, node))
        {
            _focusCheckOf = null;
            check.Loaded += (_, _) => check.Focus(FocusState.Keyboard);
        }

        return check;
    }

    private void Describe(Button button, DiskMapScreen screen, MapBox box)
    {
        if (_focusFirstBox)
        {
            _focusFirstBox = false;
            button.Loaded += (_, _) => button.Focus(FocusState.Keyboard);
        }

        void Show()
        {
            long shown = Math.Max(1, screen.Boxes(1, 1).Sum(item => item.Item.Bytes));
            int share = Math.Max(1, (int)Math.Round(100d * box.Item.Bytes / shown));
            string where = box.Item.Node?.FullPath ?? NameOf(box.Item);
            SetNote(_localizer.Format("DiskMapHoverFormat", where, DiskTexts.Size(_localizer, box.Item.Bytes), share), warning: false);
        }

        button.PointerEntered += (_, _) => Show();
        button.GotFocus += (_, _) => Show();
    }

    private void Open(DiskMapScreen screen, MapBox box, Button source)
    {
        if (box.Item.Node is not { } node)
        {
            SetNote(_localizer.Get(box.Item.Kind == MapItemKind.Unread ? "DiskMapUnreadNote" : "DiskMapRestNote"), warning: false);
            return;
        }

        if (screen.CanEnter(node))
        {
            Point center = source.TransformToVisual(MapCanvas).TransformPoint(new Point(source.ActualWidth / 2, source.ActualHeight / 2));
            _focusFirstBox = source.FocusState == FocusState.Keyboard;
            screen.Enter(node);
            PlayMove(DiveFromScale, new Vector2((float)center.X, (float)center.Y));
            Render();
            return;
        }

        MapLock mapLock = screen.LockOf(node);
        if (mapLock != MapLock.None)
        {
            SetNote(_localizer.Get($"DiskMapLock_{mapLock}"), warning: true);
            return;
        }

        if (screen.IsSelected(node))
        {
            screen.Unselect(node);
        }
        else
        {
            screen.TrySelect(node);
        }

        Render();
    }

    private void RenderList(DiskMapScreen screen)
    {
        ListPanel.Children.Clear();
        IReadOnlyList<MapBox> items = screen.Boxes(1, 1);
        long largest = items.Count > 0 ? items.Max(item => item.Item.Bytes) : 0;
        foreach (MapBox item in items)
        {
            ListPanel.Children.Add(ListRow(screen, item, largest));
        }
    }

    private Grid ListRow(DiskMapScreen screen, MapBox item, long largest)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        if (item.Item.Node is { } node)
        {
            FrameworkElement mark = screen.LockOf(node) == MapLock.None
                ? SelectBox(screen, node)
                : new FontIcon { Glyph = "\uE72E", FontSize = 13, Foreground = Themed("InkSecondaryBrush") };
            row.Children.Add(mark);
        }

        var open = new Button
        {
            Style = (Style)Resources["MapBoxStyle"],
            Padding = new Thickness(4, 6, 4, 6),
            Content = new TextBlock { Text = NameOf(item.Item), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        AutomationProperties.SetName(open, _localizer.Format("DiskMapItemFormat", NameOf(item.Item), DiskTexts.Size(_localizer, item.Item.Bytes)));
        open.Click += (sender, _) => Open(screen, item, (Button)sender);
        Remember(item.Item, open);
        Grid.SetColumn(open, 1);
        row.Children.Add(open);

        var size = new TextBlock
        {
            Text = DiskTexts.Size(_localizer, item.Item.Bytes),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = Themed("InkSecondaryBrush"),
        };
        Grid.SetColumn(size, 2);
        row.Children.Add(size);

        Grid bar = ShareBar(item, largest);
        Grid.SetColumn(bar, 3);
        row.Children.Add(bar);
        return row;
    }

    private static Grid ShareBar(MapBox item, long largest)
    {
        double share = largest > 0 ? (double)item.Item.Bytes / largest : 0;
        var bar = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(3), Background = Themed("LineBrush") };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - share, 0.001), GridUnitType.Star) });
        bar.Children.Add(new Border { Background = Themed(ContrastMode.IsOn ? "InkBrush" : "AccentBrush") });
        return bar;
    }

    private void RenderSelection(DiskMapScreen screen)
    {
        ChipPanel.Children.Clear();
        IReadOnlyList<FolderNode> picked = screen.Preview();
        foreach (FolderNode node in picked.Take(ChipsShown))
        {
            ChipPanel.Children.Add(Chip(screen, node));
        }

        if (picked.Count > ChipsShown)
        {
            ChipPanel.Children.Add(new TextBlock
            {
                Text = _localizer.Format("DiskMapMoreFormat", picked.Count - ChipsShown),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Themed("InkSecondaryBrush"),
            });
        }

        SelectionSummary.Text = picked.Count > 0
            ? _localizer.Format("DiskMapSelectedFormat", picked.Count, DiskTexts.Size(_localizer, screen.SelectedBytes))
            : _localizer.Get("DiskMapNothingSelected");
        MoveButton.IsEnabled = picked.Count > 0 && !_moving;
    }

    private async void OnMoveClick(object sender, RoutedEventArgs e)
    {
        if (_screen is not { SelectedCount: > 0 } screen || !await ConfirmAsync(screen))
        {
            return;
        }

        int selectedBefore = screen.SelectedCount;
        var progress = new Progress<MoveProgress>(step => ScanPercent.Text = step.Fraction.ToString("P0", _localizer.Culture));
        try
        {
            Dictionary<string, PulledItem> pulled = screen.Preview().ToDictionary(
                node => node.FullPath,
                node => new PulledItem(StartOf(node), DiskTexts.ShortName(node.FullPath)),
                StringComparer.OrdinalIgnoreCase);
            ResultPanel.Visibility = Visibility.Collapsed;
            SetMoving(true);
            MoveOutcome outcome = await Task.Run(() => screen.MoveSelected(_runner, _recycleBin, progress));
            ShowMapAgain();
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
            string message = _localizer.Format("DiskMoveErrorFormat", exception.Message);
            ShowMapAgain();
            ShowResult(message, warning: true);
            Announcer.Say(ScreenTitle, message);
        }
        finally
        {
            SetMoving(false);
        }

        if (screen.SelectedCount < selectedBefore)
        {
            Moved?.Invoke(this, EventArgs.Empty);
        }
    }

    // Whatever is selected may be the user's own and the map cannot tell (vacuo-kurallar 12): the warning always shows.
    private Task<bool> ConfirmAsync(DiskMapScreen screen)
    {
        IReadOnlyList<FolderNode> preview = screen.Preview();
        var parts = new List<UIElement>
        {
            ConfirmCard.Warning(_localizer.Format("DiskMapPreviewWarningFormat", preview.Count)),
            ConfirmCard.List(preview.Select(node => new ConfirmItem(
                DiskRing.ColorOf(Bucket.Yours),
                NameOf(node),
                node.FullPath,
                DiskTexts.Size(_localizer, node.Bytes)))),
            ConfirmCard.Note(_localizer.Get("DiskPreviewRecycleNote")),
        };
        return Confirm.AskAsync(new ConfirmRequest(
            _localizer.Get("DiskPreviewTitle"),
            _localizer.Format("DiskPreviewFormat", preview.Count, DiskTexts.Size(_localizer, screen.SelectedBytes)),
            _localizer.Get("DiskConfirmMove"),
            _localizer.Get("DiskPreviewClose"),
            parts));
    }

    private void SetMoving(bool moving)
    {
        _moving = moving;
        BackButton.IsEnabled = !moving;
        RescanButton.IsEnabled = !moving;
        JournalButton.IsEnabled = !moving;
        MapPill.IsEnabled = !moving;
        ListPill.IsEnabled = !moving;
        if (moving)
        {
            ShowProgress(_localizer.Get("DiskMoving"), canStop: false);
            TrailPanel.Visibility = Visibility.Collapsed;
            ChipPanel.Visibility = Visibility.Collapsed;
            SelectionSummary.Text = _localizer.Get("DiskMoving");
            MoveButton.IsEnabled = false;
        }
        else if (_screen is { } screen)
        {
            RenderSelection(screen);
        }
    }

    private void ShowMapAgain()
    {
        ScanPanel.Visibility = Visibility.Collapsed;
        TrailPanel.Visibility = Visibility.Visible;
        ChipPanel.Visibility = Visibility.Visible;
        Render();
    }

    // Items in another folder, or scrolled out of sight, start from the nearest edge of the map.
    private Vector2 StartOf(FolderNode node)
    {
        Rect frame = MapFrame.TransformToVisual(this).TransformBounds(new Rect(0, 0, MapFrame.ActualWidth, MapFrame.ActualHeight));
        if (!_drawn.TryGetValue(node, out FrameworkElement? element))
        {
            return new Vector2((float)(frame.Left + frame.Width / 2), (float)frame.Bottom);
        }

        Point center = element.TransformToVisual(this).TransformPoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        return new Vector2((float)center.X, (float)Math.Clamp(center.Y, frame.Top, frame.Bottom));
    }

    private async Task PlayBurstAsync(IReadOnlyList<PulledItem> items)
    {
        // A contrast theme skips it like animations off: the burst is drawn in fixed VOID colours (Aşama 3f).
        if (!_animationsEnabled || ContrastMode.IsOn || items.Count == 0)
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
        ShowResult(DiskTexts.Outcome(_localizer, outcome), warning: outcome.Failed.Count > 0);
        if (outcome.Moved.Count > 0)
        {
            ResultLines.Children.Add(ResultLine(_localizer.Get("DiskMovedNote"), "InkSecondaryBrush"));
        }

        foreach (MoveFailure failure in outcome.Failed)
        {
            ResultLines.Children.Add(ResultLine($"{DiskTexts.FailureReason(_localizer, failure.Reason)} · {failure.Path}", "InkSecondaryBrush"));
        }

        Announcer.Say(ScreenTitle, DiskTexts.Outcome(_localizer, outcome));
    }

    // Under the map, where pointing at a box does not replace it.
    private void ShowResult(string text, bool warning)
    {
        ResultLines.Children.Clear();
        ResultLines.Children.Add(ResultLine(text, warning ? "WarningBrush" : "InkBrush"));
        ResultPanel.Visibility = Visibility.Visible;
    }

    private static TextBlock ResultLine(string text, string brush) =>
        new() { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Themed(brush) };

    private Button Chip(DiskMapScreen screen, FolderNode node)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        line.Children.Add(new TextBlock { Text = node.Name, FontSize = 12, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis });
        line.Children.Add(new FontIcon { Glyph = "\uE711", FontSize = 10, Foreground = Themed("InkSecondaryBrush") });
        var chip = new Button
        {
            Style = (Style)Resources["MapBoxStyle"],
            Padding = new Thickness(10, 3, 8, 3),
            CornerRadius = new CornerRadius(13),
            BorderThickness = new Thickness(1),
            BorderBrush = Themed("LineBrush"),
            Background = Themed("SurfaceRaisedBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = line,
        };
        AutomationProperties.SetName(chip, _localizer.Format("DiskMapUnselectFormat", node.Name));
        chip.Click += (_, _) =>
        {
            screen.Unselect(node);
            Render();
        };
        return chip;
    }

    private string NameOf(MapItem item) => item.Kind switch
    {
        MapItemKind.Rest => _localizer.Get("DiskMapRest"),
        MapItemKind.Unread => _localizer.Get("DiskMapUnread"),
        _ => NameOf(item.Node!),
    };

    // The root's name is its path: "C:" rather than "C:\".
    private static string NameOf(FolderNode node) => node.Parent is null ? node.Name.TrimEnd(Path.DirectorySeparatorChar) : node.Name;

    private void ShowPills()
    {
        StylePill(MapPill, !_showingList);
        StylePill(ListPill, _showingList);
    }

    private static void StylePill(Button pill, bool on)
    {
        pill.Background = Themed(on ? "StrongPressedFillBrush" : "GhostFillBrush");
        pill.BorderBrush = Themed(on ? "HoverBorderBrush" : "GhostBorderBrush");
        pill.Foreground = Themed(on ? "InkBrush" : "InkSecondaryBrush");
        AutomationProperties.SetItemStatus(pill, on ? "\u2713" : string.Empty);
    }

    private static StackPanel PillContent(string glyph, string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13 });
        content.Children.Add(new TextBlock { Text = text, FontSize = 13 });
        return content;
    }

    private void SetNote(string text, bool warning)
    {
        NoteText.Text = text;
        NoteText.Foreground = Themed(warning ? "WarningBrush" : "InkSecondaryBrush");
    }

    // Going in grows the map out of the box that was clicked; on the compositor, so it never holds the UI up.
    private void PlayMove(float fromScale, Vector2 center)
    {
        if (!_animationsEnabled || _showingList)
        {
            return;
        }

        Microsoft.UI.Composition.Visual visual = ElementCompositionPreview.GetElementVisual(MapCanvas);
        Microsoft.UI.Composition.Compositor compositor = visual.Compositor;
        visual.CenterPoint = new Vector3(center, 0);
        Microsoft.UI.Composition.CubicBezierEasingFunction easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.7f), new Vector2(0.2f, 1f));

        Microsoft.UI.Composition.Vector3KeyFrameAnimation scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, new Vector3(fromScale, fromScale, 1));
        scale.InsertKeyFrame(1, Vector3.One, easing);
        scale.Duration = DiveDuration;
        visual.StartAnimation("Scale", scale);

        Microsoft.UI.Composition.ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0.15f);
        fade.InsertKeyFrame(1, 1f, easing);
        fade.Duration = DiveDuration;
        visual.StartAnimation("Opacity", fade);
    }

    private static Brush Themed(string key) => (Brush)Application.Current.Resources[key];

    private void OnMapFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        MapFrame.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        if (_screen is not null && !_showingList && ScanPanel.Visibility == Visibility.Collapsed)
        {
            RenderMap(_screen);
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_screen is { Trail.Count: > 1 } screen && _scan is null)
        {
            screen.Up();
            PlayMove(RiseFromScale, new Vector2((float)MapFrame.ActualWidth / 2, (float)MapFrame.ActualHeight / 2));
            Render();
            return;
        }

        _scan?.Cancel();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRescanClick(object sender, RoutedEventArgs e) => StartScan();

    private void OnJournalClick(object sender, RoutedEventArgs e) => JournalRequested?.Invoke(this, EventArgs.Empty);

    private void OnBurstUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args) =>
        _burst?.Advance(args.Timing.ElapsedTime);

    private void OnBurstDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args) =>
        _burst?.Draw(args.DrawingSession);

    private void OnStopClick(object sender, RoutedEventArgs e) => _scan?.Cancel();

    private void OnMapPillClick(object sender, RoutedEventArgs e)
    {
        _showingList = false;
        Render();
    }

    private void OnListPillClick(object sender, RoutedEventArgs e)
    {
        _showingList = true;
        Render();
    }
}
