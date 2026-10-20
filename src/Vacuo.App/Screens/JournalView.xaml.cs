using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Actions;
using Vacuo.Core.Journal;
using Windows.UI;

namespace Vacuo.App.Screens;

/// <summary>
/// The Journal screen: everything Vacuo moved, newest first, each with "Undo", and the administrator
/// cleanups, which cannot be undone (ADR-003). All decisions live
/// in <see cref="JournalScreen"/>; this view shows them and puts items back off the UI thread.
/// </summary>
public sealed partial class JournalView : UserControl
{
    private const int PageSize = 50;

    private Localizer _localizer = null!;
    private JournalScreen _screen = null!;
    private IReadOnlyList<JournalRow> _rows = [];
    private int _shown;
    private Button? _moreButton;

    public JournalView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    /// <summary>Raised after an item was put back, so the Disk screen can scan again.</summary>
    internal event EventHandler? Restored;

    internal void Initialize(Localizer localizer, JournalScreen screen)
    {
        _localizer = localizer;
        _screen = screen;
        BackButton.Content = localizer.Get("DiskBackButton");
        ScreenTitle.Text = localizer.Get("JournalTitle");
        Intro.Text = localizer.Get("JournalIntro");
    }

    /// <summary>
    /// Reads the journal off the UI thread and lists the newest rows; a long journal built all at once
    /// froze the window on every visit (400 rows, 20 October 2026).
    /// </summary>
    internal async Task LoadAsync()
    {
        IReadOnlyList<JournalRow> rows;
        try
        {
            rows = await Task.Run(() => _screen.Rows);
        }
        catch (JournalCorruptException exception)
        {
            EntryList.Children.Clear();
            EntryList.Children.Add(Text(_localizer.Format("JournalReadErrorFormat", exception.Message), "WarningBrush"));
            return;
        }

        _rows = rows;
        _shown = 0;
        EntryList.Children.Clear();
        if (rows.Count == 0)
        {
            EntryList.Children.Add(Text(_localizer.Get("JournalEmpty"), "InkSecondaryBrush"));
            return;
        }

        ShowMore();
    }

    // Older rows come a page at a time, only when asked for.
    private void ShowMore()
    {
        if (_moreButton is not null)
        {
            EntryList.Children.Remove(_moreButton);
            _moreButton = null;
        }

        foreach (JournalRow row in _rows.Skip(_shown).Take(PageSize))
        {
            EntryList.Children.Add(row.BatchId is { } batchId ? BatchRow(row, batchId) : Row(row.First));
        }

        _shown = Math.Min(_rows.Count, _shown + PageSize);
        if (_shown < _rows.Count)
        {
            _moreButton = new Button
            {
                Content = _localizer.Format("JournalMoreFormat", _rows.Count - _shown),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _moreButton.Click += (_, _) => ShowMore();
            EntryList.Children.Add(_moreButton);
        }
    }

    // A whole Düzen run, the copies one duplicate files move sent to the Recycle Bin, or the folders a removed
    // program left behind, on one row (B-161): where they were, how many, and one "Undo" for all of them.
    private Grid BatchRow(JournalRow batch, Guid batchId)
    {
        bool moved = batch.UndoneAt is null;
        bool organize = batch.BatchKind == JournalBatchKind.Organize;
        Grid row = RowFrame(moved ? "AccentBrush" : "LineBrush");
        var details = new StackPanel { Padding = new Thickness(0, 10, 0, 10), Spacing = 2 };
        if (organize)
        {
            details.Children.Add(Text(_localizer.Format("JournalOrganizeFormat", Path.GetFileName(batch.Folder)), "InkBrush"));
            details.Children.Add(Text(batch.Folder, "InkSecondaryBrush", fontSize: 12));
        }
        else
        {
            string title = batch.BatchKind == JournalBatchKind.Leftovers ? "JournalLeftovers" : "DuplicatesTitle";
            details.Children.Add(Text(_localizer.Get(title), "InkBrush"));
            details.Children.Add(Text(string.Join(" · ", batch.Folders), "InkSecondaryBrush", fontSize: 12));
        }

        string when = batch.At.ToLocalTime().ToString("g", _localizer.Culture);
        string status = moved
            ? _localizer.Get(organize ? "JournalOrganized" : "JournalInBin")
            : _localizer.Format("JournalUndoneFormat", batch.UndoneAt!.Value.ToLocalTime().ToString("g", _localizer.Culture));
        string count = batch.BatchKind == JournalBatchKind.Leftovers ? "JournalFoldersFormat" : "JournalFilesFormat";
        details.Children.Add(Text(
            $"{_localizer.Format(count, batch.FileCount)} · {DiskTexts.Size(_localizer, batch.SizeBytes)} · {when} · {status}",
            "InkSecondaryBrush",
            fontSize: 12));
        TextBlock message = HiddenMessage();
        details.Children.Add(message);
        Grid.SetColumn(details, 1);
        row.Children.Add(details);

        if (moved)
        {
            Button undo = UndoButton();
            undo.Click += async (_, _) => await UndoBatchAsync(batchId, undo, message);
            Grid.SetColumn(undo, 2);
            row.Children.Add(undo);
        }

        return row;
    }

    private async Task UndoBatchAsync(Guid batchId, Button undo, TextBlock message)
    {
        undo.IsEnabled = false;
        undo.Content = _localizer.Get("JournalUndoing");
        string? problem;
        try
        {
            IReadOnlyList<BatchUndoFailure> failures = await Task.Run(() => _screen.UndoBatch(batchId));
            problem = failures.Count == 0
                ? null
                : _localizer.Format("OrganizeUndoFailedFormat", failures.Count, DiskTexts.UndoFailure(_localizer, failures[0].Reason));
        }
        catch (Exception exception)
        {
            // Unexpected (e.g. the journal could not be written): shown on the row, never a crash.
            problem = $"{DiskTexts.UndoFailure(_localizer, UndoFailureReason.Refused)}: {exception.Message}";
        }

        Restored?.Invoke(this, EventArgs.Empty);
        if (problem is null)
        {
            await LoadAsync();
            return;
        }

        message.Text = problem;
        message.Visibility = Visibility.Visible;
        undo.Content = _localizer.Get("JournalUndoButton");
        undo.IsEnabled = true;
    }

    private static Grid RowFrame(string edgeBrush)
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
        row.Children.Add(new Border { Background = Resource<Brush>(edgeBrush) });
        return row;
    }

    private Button UndoButton() => new()
    {
        Content = _localizer.Get("JournalUndoButton"),
        Style = Resource<Style>("VoidGhostButtonStyle"),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Padding = new Thickness(18, 8, 18, 8),
    };

    private static TextBlock HiddenMessage()
    {
        TextBlock message = Text(string.Empty, "WarningBrush", fontSize: 12);
        message.Visibility = Visibility.Collapsed;
        return message;
    }

    // Same language as the confirm card: a rounded row with an edge that is lit while the item is in the
    // bin and warns when it was deleted for good.
    private Grid Row(JournalEntry entry)
    {
        bool deletedForGood = entry.Undo is DeletedForGood;
        bool inBin = entry.UndoneAt is null && !deletedForGood;
        Grid row = RowFrame(deletedForGood ? "WarningBrush" : inBin ? "AccentBrush" : "LineBrush");

        var details = new StackPanel { Padding = new Thickness(0, 10, 0, 10), Spacing = 2 };
        details.Children.Add(Text(DiskTexts.ShortName(entry.Undo.OriginalPath), "InkBrush"));
        details.Children.Add(Text(entry.Undo.OriginalPath, "InkSecondaryBrush", fontSize: 12));
        string when = entry.At.ToLocalTime().ToString("g", _localizer.Culture);
        string status = deletedForGood ? _localizer.Get("JournalDeletedForGood")
            : inBin ? _localizer.Get("JournalInBin")
            : _localizer.Format("JournalUndoneFormat", entry.UndoneAt!.Value.ToLocalTime().ToString("g", _localizer.Culture));
        // DISM reports no size, so its entry shows none rather than "0 bytes".
        string size = deletedForGood && entry.SizeBytes == 0 ? string.Empty : $"{DiskTexts.Size(_localizer, entry.SizeBytes)} · ";
        details.Children.Add(Text($"{size}{when} · {status}", "InkSecondaryBrush", fontSize: 12));
        TextBlock message = HiddenMessage();
        details.Children.Add(message);
        Grid.SetColumn(details, 1);
        row.Children.Add(details);

        if (inBin)
        {
            Button undo = UndoButton();
            undo.Click += async (_, _) => await UndoAsync(entry, undo, message);
            Grid.SetColumn(undo, 2);
            row.Children.Add(undo);
        }

        return row;
    }

    private async Task UndoAsync(JournalEntry entry, Button undo, TextBlock message)
    {
        undo.IsEnabled = false;
        undo.Content = _localizer.Get("JournalUndoing");
        UndoFailureReason? failure;
        string? detail = null;
        try
        {
            failure = await Task.Run(() => _screen.Undo(entry.Id));
        }
        catch (Exception exception)
        {
            // Unexpected (e.g. the journal could not be written): shown on the row, never a crash.
            failure = UndoFailureReason.Refused;
            detail = exception.Message;
        }

        if (failure is null)
        {
            await LoadAsync();
            Restored?.Invoke(this, EventArgs.Empty);
            return;
        }

        string reason = DiskTexts.UndoFailure(_localizer, failure.Value);
        message.Text = detail is null ? reason : $"{reason}: {detail}";
        message.Visibility = Visibility.Visible;
        undo.Content = _localizer.Get("JournalUndoButton");
        undo.IsEnabled = failure != UndoFailureReason.AlreadyUndone;
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
}
