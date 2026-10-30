using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.App.Visuals;
using Vacuo.Core.DiskMap;
using Vacuo.Core.Notices;
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
    private const int ChipsShown = 2;

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DiveDuration = TimeSpan.FromMilliseconds(240);
    private static readonly Color Ink = Color.FromArgb(0xFF, 0xEC, 0xE7, 0xF5);
    private static readonly Color InkSecondary = Color.FromArgb(0xFF, 0xAB, 0xA1, 0xBF);
    private static readonly Color Void = Color.FromArgb(0xFF, 0x0A, 0x08, 0x11);
    private static readonly Color Quiet = Color.FromArgb(0xFF, 0x1D, 0x17, 0x2A);

    // From "takes little" to "takes a lot"; the two brightest carry dark text.
    private static readonly Color[] HeatColors =
    [
        Color.FromArgb(0xFF, 0x2A, 0x21, 0x40),
        Color.FromArgb(0xFF, 0x3D, 0x31, 0x63),
        Color.FromArgb(0xFF, 0x4A, 0x3F, 0x66),
        Color.FromArgb(0xFF, 0x66, 0x50, 0xB4),
        Color.FromArgb(0xFF, 0x9D, 0x7C, 0xFF),
        Color.FromArgb(0xFF, 0xD4, 0xC6, 0xFF),
    ];

    private readonly MapProtection _protection = new(MapProtectedPaths.ForThisMachine());
    private Localizer _localizer = null!;
    private bool _animationsEnabled;
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

    internal void Initialize(Localizer localizer, bool animationsEnabled)
    {
        _localizer = localizer;
        _animationsEnabled = animationsEnabled;
        ScreenTitle.Text = localizer.Get("DiskMapTitle");
        BackButton.Content = localizer.Get("DiskBackButton");
        RescanButton.Content = localizer.Get("DiskRescanButton");
        StopButton.Content = localizer.Get("DiskMapStop");
        ScanNote.Text = localizer.Get("DiskMapScanning");
        RampLess.Text = localizer.Get("DiskMapRampLess");
        RampMore.Text = localizer.Get("DiskMapRampMore");
        MapPill.Content = PillContent("\uE80A", localizer.Get("DiskMapPillMap"));
        ListPill.Content = PillContent("\uE8FD", localizer.Get("DiskMapPillList"));
        AutomationProperties.SetName(MapPill, localizer.Get("DiskMapPillMap"));
        AutomationProperties.SetName(ListPill, localizer.Get("DiskMapPillList"));
        ShowPills();
    }

    /// <summary>A scan does not outlive the window.</summary>
    internal void Shutdown() => _scan?.Cancel();

    /// <summary>Shows the last scan while it is fresh, or scans.</summary>
    internal void Refresh()
    {
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

        ScanPercent.Text = 0d.ToString("P0", _localizer.Culture);
        ScanPanel.Visibility = Visibility.Visible;
        MapCanvas.Visibility = Visibility.Collapsed;
        ListScroller.Visibility = Visibility.Collapsed;
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

    private void Render()
    {
        ShowPills();
        if (_screen is not { } screen)
        {
            UsedText.Text = string.Empty;
            TrailPanel.Children.Clear();
            MapCanvas.Children.Clear();
            ListPanel.Children.Clear();
            ChipPanel.Children.Clear();
            SelectionSummary.Text = string.Empty;
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
        tile.Children.Add(button);

        if (box.Item.Node is { } item && tile.Width >= SmallestMarkedBox && tile.Height >= SmallestMarkedBox)
        {
            AddMark(tile, screen, item, text);
        }

        return tile;
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

        return (new SolidColorBrush(HeatColors[box.Heat]), box.Heat >= HeatColors.Length - 2 ? Void : Ink);
    }

    // What takes the most space is drawn as the black hole that pulls the disk in.
    private static RadialGradientBrush CoreBrush()
    {
        var brush = new RadialGradientBrush { Center = new Point(0.5, 0.54), GradientOrigin = new Point(0.5, 0.54), RadiusX = 0.72, RadiusY = 0.72 };
        brush.GradientStops.Add(new GradientStop { Color = Void, Offset = 0.14 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xFF, 0x1B, 0x14, 0x30), Offset = 0.24 });
        brush.GradientStops.Add(new GradientStop { Color = HeatColors[2], Offset = 0.44 });
        brush.GradientStops.Add(new GradientStop { Color = HeatColors[3], Offset = 0.66 });
        brush.GradientStops.Add(new GradientStop { Color = HeatColors[4], Offset = 1 });
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
        bar.Children.Add(new Border { Background = ContrastMode.IsOn ? Themed("InkBrush") : new SolidColorBrush(HeatColors[Math.Max(item.Heat, 2)]) });
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
    }

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
