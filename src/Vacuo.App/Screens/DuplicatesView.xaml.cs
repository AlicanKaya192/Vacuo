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
using Vacuo.Core.Duplicates;
using Vacuo.Core.Scanning;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Vacuo.App.Screens;

/// <summary>
/// The duplicate files screen (Aşama 3d B): the folders searched, groups of files with the same content,
/// the copies to pick, the confirm step and the move. All decisions live in <see cref="DuplicatesScreen"/>;
/// this view shows them and runs the search and the move off the UI thread. Nothing starts picked, one
/// copy of each group always stays, and nothing moves without the user confirming.
/// </summary>
public sealed partial class DuplicatesView : UserControl
{
    private static readonly TimeSpan CountUpDuration = TimeSpan.FromMilliseconds(700);

    // A long list built at once froze the Journal; groups come a page at a time.
    private const int GroupsPerPage = 40;
    private const string DevDriveFormat = "ReFS";

    private readonly Dictionary<string, FrameworkElement> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CheckBox> _checks = [];
    private Localizer _localizer = null!;
    private ActionRunner _runner = null!;
    private IRecycleBin _recycleBin = null!;
    private nint _window;
    private bool _animationsEnabled;
    private DuplicateRoots _roots = null!;
    private DuplicatesScreen? _screen;
    private CancellationTokenSource? _search;
    private int _shownGroups;
    private Button? _moreButton;
    private bool _moving;
    private volatile PullBurst? _burst;

    public DuplicatesView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? JournalRequested;

    internal void Initialize(Localizer localizer, ActionRunner runner, IRecycleBin recycleBin, nint window, bool animationsEnabled)
    {
        _localizer = localizer;
        _runner = runner;
        _recycleBin = recycleBin;
        _window = window;
        _animationsEnabled = animationsEnabled;
        _roots = new DuplicateRoots(DuplicateFinder.DefaultRoots().Where(Directory.Exists), ScanRootGuard.ForThisMachine());
        ScreenTitle.Text = localizer.Get("DuplicatesTitle");
        BackButton.Content = localizer.Get("DiskBackButton");
        JournalButton.Content = localizer.Get("DiskJournalButton");
        RescanButton.Content = localizer.Get("DiskRescanButton");
        FoldersTitle.Text = localizer.Get("DuplicatesFoldersTitle");
        AddFolderButton.Content = localizer.Get("DuplicatesAddFolderButton");
        MoveButton.Content = localizer.Get("DiskMoveButton");
        MovedNote.Text = localizer.Get("DiskMovedNote");
        Confirm.Initialize(animationsEnabled);
        ShowFolders(note: null);
    }

    /// <summary>Stops Win2D's game-loop threads before the window goes away.</summary>
    internal void Shutdown()
    {
        _search?.Cancel();
        BurstCanvas.RemoveFromVisualTree();
        Confirm.Shutdown();
    }

    /// <summary>Searches the first time the screen opens; later visits keep the last result until Rescan.</summary>
    internal void Refresh()
    {
        if (_screen is null && _search is null)
        {
            Search();
        }
    }

    /// <summary>Copies put back from the Journal are duplicates again, so the list is searched anew.</summary>
    internal void Rescan() => Search();

    // A new search replaces one still running: the folders it was given are out of date.
    private async void Search()
    {
        if (_moving)
        {
            return;
        }

        _search?.Cancel();
        var search = new CancellationTokenSource();
        _search = search;
        IReadOnlyList<string> folders = _roots.Folders.ToList();
        Confirm.Cancel();
        _screen = null;
        ResultPanel.Visibility = Visibility.Collapsed;
        ShowMessage(_localizer.Get("DuplicatesSearching"));
        try
        {
            DuplicateScan scan = await Task.Run(() => DuplicateFinder.Find(folders, DuplicateFinder.DefaultMinimumBytes, search.Token));
            if (search.IsCancellationRequested)
            {
                return;
            }

            _screen = new DuplicatesScreen(scan);
            RenderList();
        }
        catch (OperationCanceledException)
        {
            // A newer search took over and shows its own result.
        }
        catch (Exception exception)
        {
            ShowMessage(_localizer.Format("DuplicatesSearchFailedFormat", exception.Message));
        }
        finally
        {
            if (_search == search)
            {
                _search = null;
            }

            search.Dispose();
        }
    }

    private void ShowFolders(string? note)
    {
        FolderList.Children.Clear();
        foreach (string folder in _roots.Folders)
        {
            FolderList.Children.Add(FolderRow(folder));
        }

        List<string> notes = [_localizer.Get("DuplicatesHint")];
        if (_roots.Folders.Any(IsOnDevDrive))
        {
            notes.Add(_localizer.Get("DuplicatesDevDriveNote"));
        }

        if (note is not null)
        {
            notes.Add(note);
        }

        FolderNote.Text = string.Join(" ", notes);
    }

    private Grid FolderRow(string folder)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock path = BodyText(folder);
        path.TextWrapping = TextWrapping.NoWrap;
        path.TextTrimming = TextTrimming.CharacterEllipsis;
        path.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(path);

        var remove = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 10 },
            Style = (Style)Application.Current.Resources["VoidIconButtonStyle"],
            IsEnabled = _roots.Folders.Count > 1 && !_moving,
        };
        AutomationProperties.SetName(remove, _localizer.Format("DuplicatesRemoveFolderFormat", folder));
        remove.Click += (_, _) =>
        {
            if (_roots.Remove(folder))
            {
                ShowFolders(note: null);
                Search();
            }
        };
        Grid.SetColumn(remove, 1);
        row.Children.Add(remove);
        return row;
    }

    // Dev Drive shares the blocks of copied files, so a copy there may take up no extra space.
    private static bool IsOnDevDrive(string folder)
    {
        try
        {
            return new DriveInfo(folder).DriveFormat == DevDriveFormat;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private void ShowMessage(string message)
    {
        ClearList();
        GroupList.Children.Add(BodyText(message));
        UpdateSelectionSummary();
    }

    private void RenderList()
    {
        ClearList();
        if (_screen!.Groups.Count == 0)
        {
            GroupList.Children.Add(BodyText(_localizer.Get("DuplicatesNothingFound")));
        }
        else
        {
            TextBlock summary = BodyText(_localizer.Format(
                "DuplicatesSummaryFormat", _screen.Groups.Count, DiskTexts.Size(_localizer, _screen.WastedBytes)));
            summary.Foreground = (Brush)Application.Current.Resources["InkBrush"];
            GroupList.Children.Add(summary);
        }

        if (_screen.SkippedEntries > 0)
        {
            GroupList.Children.Add(BodyText(_localizer.Format("DuplicatesSkippedFormat", _screen.SkippedEntries)));
        }

        ShowMoreGroups();
        UpdateSelectionSummary();
    }

    private void ShowMoreGroups()
    {
        if (_moreButton is not null)
        {
            GroupList.Children.Remove(_moreButton);
            _moreButton = null;
        }

        foreach (DuplicateGroup group in _screen!.Groups.Skip(_shownGroups).Take(GroupsPerPage))
        {
            GroupList.Children.Add(GroupSection(group));
        }

        _shownGroups = Math.Min(_screen.Groups.Count, _shownGroups + GroupsPerPage);
        if (_shownGroups < _screen.Groups.Count)
        {
            _moreButton = new Button
            {
                Content = _localizer.Format("DuplicatesMoreFormat", _screen.Groups.Count - _shownGroups),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _moreButton.Click += (_, _) => ShowMoreGroups();
            GroupList.Children.Add(_moreButton);
        }
    }

    private void ClearList()
    {
        _rows.Clear();
        _checks.Clear();
        _moreButton = null;
        _shownGroups = 0;
        GroupList.Children.Clear();
    }

    private StackPanel GroupSection(DuplicateGroup group)
    {
        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock
        {
            Text = _localizer.Format(
                "DuplicatesGroupFormat", Path.GetFileName(group.Files[0].Path), DiskTexts.Size(_localizer, group.FileBytes), group.Files.Count),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Style = (Style)Application.Current.Resources["CardTitleStyle"],
        });

        foreach (DuplicateFile file in group.Files)
        {
            section.Children.Add(Row(file));
        }

        return section;
    }

    private Grid Row(DuplicateFile file)
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

        // Copies that failed to move stay picked, so the user can try them again.
        var check = new CheckBox
        {
            Tag = file.Path,
            MinWidth = 0,
            IsChecked = _screen!.IsPicked(file.Path),
            IsEnabled = _screen.CanPick(file.Path),
        };
        AutomationProperties.SetName(check, file.Path);
        check.Checked += OnRowChecked;
        check.Unchecked += OnRowUnchecked;
        row.Children.Add(check);
        _checks.Add(check);

        var name = new StackPanel();
        name.Children.Add(new TextBlock
        {
            Text = Path.GetDirectoryName(file.Path) ?? file.Path,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["InkBrush"],
        });
        name.Children.Add(BodyText(file.LastWriteTimeUtc.ToLocalTime().ToString("g", _localizer.Culture)));
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        _rows[file.Path] = row;
        return row;
    }

    private static TextBlock BodyText(string text) =>
        new() { Text = text, Style = (Style)Application.Current.Resources["CardBodyStyle"] };

    private void UpdateSelectionSummary()
    {
        int count = _screen?.PickedCount ?? 0;
        SelectionSummary.Text = count == 0
            ? _localizer.Get("DuplicatesSelectionEmpty")
            : _localizer.Format("DuplicatesSelectionFormat", count, DiskTexts.Size(_localizer, _screen!.PickedBytes));
        MoveButton.IsEnabled = count > 0 && !_moving;
    }

    // The last unpicked copy of a group cannot be picked; every pick or unpick can change which one that is.
    private void UpdateChecks()
    {
        foreach (CheckBox check in _checks)
        {
            check.IsEnabled = !_moving && _screen is not null && _screen.CanPick((string)check.Tag);
        }
    }

    private void SetMoving(bool moving)
    {
        _moving = moving;
        BackButton.IsEnabled = !moving;
        RescanButton.IsEnabled = !moving;
        JournalButton.IsEnabled = !moving;
        AddFolderButton.IsEnabled = !moving;
        ShowFolders(note: null);
        UpdateChecks();
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
        var check = (CheckBox)sender;
        if (_screen is not null && !_screen.TryPick((string)check.Tag))
        {
            check.IsChecked = false;
        }

        UpdateChecks();
        UpdateSelectionSummary();
    }

    private void OnRowUnchecked(object sender, RoutedEventArgs e)
    {
        _screen?.Unpick((string)((CheckBox)sender).Tag);
        UpdateChecks();
        UpdateSelectionSummary();
    }

    // The system folder picker; an unpackaged app has to tell it which window it belongs to.
    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _window);
        if (await picker.PickSingleFolderAsync() is not StorageFolder picked || _moving)
        {
            return;
        }

        switch (_roots.Add(picked.Path))
        {
            case AddFolderResult.Added:
                ShowFolders(note: null);
                Search();
                break;
            case AddFolderResult.AlreadyIncluded:
                ShowFolders(_localizer.Get("DuplicatesFolderAlreadyIncluded"));
                break;
            case AddFolderResult.SystemFolder:
                ShowFolders(_localizer.Get("DuplicatesFolderSystem"));
                break;
        }
    }

    private async void OnMoveClick(object sender, RoutedEventArgs e)
    {
        if (_screen is not { PickedCount: > 0 } screen || !await ConfirmAsync(screen))
        {
            return;
        }

        Dictionary<string, PulledItem> pulled = screen.Preview().ToDictionary(
            copy => copy.Path,
            copy => new PulledItem(RowCenter(_rows.GetValueOrDefault(copy.Path)), DiskTexts.ShortName(copy.Path)),
            StringComparer.OrdinalIgnoreCase);
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
    }

    // The copies are the user's own files (vacuo-kurallar 12): the warning and the Recycle Bin note always show.
    private Task<bool> ConfirmAsync(DuplicatesScreen screen)
    {
        IReadOnlyList<PickedCopy> preview = screen.Preview();
        var parts = new List<UIElement>
        {
            ConfirmCard.Warning(_localizer.Format("DuplicatesPreviewWarningFormat", preview.Count)),
            ConfirmCard.List(preview.Select(copy => new ConfirmItem(
                DiskRing.ColorOf(Bucket.Yours),
                Path.GetFileName(copy.Path),
                copy.Path,
                DiskTexts.Size(_localizer, copy.Bytes)))),
            ConfirmCard.Note(_localizer.Get("DuplicatesPreviewKeepNote")),
            ConfirmCard.Note(_localizer.Get("DiskPreviewRecycleNote")),
        };
        return Confirm.AskAsync(new ConfirmRequest(
            _localizer.Get("DuplicatesPreviewTitle"),
            _localizer.Format("DuplicatesPreviewFormat", preview.Count, DiskTexts.Size(_localizer, screen.PickedBytes)),
            _localizer.Get("DiskConfirmMove"),
            _localizer.Get("DiskPreviewClose"),
            parts));
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
        FailureList.Children.Clear();
        bool anyMoved = outcome.Moved.Count > 0;
        MovedCounter.Visibility = anyMoved ? Visibility.Visible : Visibility.Collapsed;
        MovedNote.Visibility = MovedCounter.Visibility;
        if (anyMoved)
        {
            CountUp(outcome.FreedBytes);
        }

        MovedLargest.Text = DiskTexts.Largest(_localizer, outcome) ?? string.Empty;
        MovedLargest.Visibility = MovedLargest.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

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
        Announcer.Say(ScreenTitle, DiskTexts.Outcome(_localizer, outcome));
    }

    private void ShowError(string message)
    {
        MovedCounter.Visibility = Visibility.Collapsed;
        MovedNote.Visibility = Visibility.Collapsed;
        MovedLargest.Visibility = Visibility.Collapsed;
        FailureList.Children.Clear();
        TextBlock error = BodyText(message);
        error.Foreground = (Brush)Application.Current.Resources["WarningBrush"];
        FailureList.Children.Add(error);
        ResultPanel.Visibility = Visibility.Visible;
        Announcer.Say(ScreenTitle, message);
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

    // Rows on a page not shown yet, or scrolled out of sight, start from the nearest edge of the list.
    private Vector2 RowCenter(FrameworkElement? row)
    {
        Rect list = ListScroller.TransformToVisual(this)
            .TransformBounds(new Rect(0, 0, ListScroller.ActualWidth, ListScroller.ActualHeight));
        if (row is null)
        {
            return new Vector2((float)(list.Left + list.Width / 2), (float)list.Bottom);
        }

        Point center = row.TransformToVisual(this).TransformPoint(new Point(row.ActualWidth / 2, row.ActualHeight / 2));
        return new Vector2((float)center.X, (float)Math.Clamp(center.Y, list.Top, list.Bottom));
    }

    private void OnBurstUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args) =>
        _burst?.Advance(args.Timing.ElapsedTime);

    private void OnBurstDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args) =>
        _burst?.Draw(args.DrawingSession);

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnRescanClick(object sender, RoutedEventArgs e) => Search();

    private void OnJournalClick(object sender, RoutedEventArgs e) => JournalRequested?.Invoke(this, EventArgs.Empty);
}
