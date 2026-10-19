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
    private Localizer _localizer = null!;
    private JournalScreen _screen = null!;

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

    internal void Show()
    {
        EntryList.Children.Clear();
        IReadOnlyList<JournalEntry> entries;
        try
        {
            entries = _screen.Entries;
        }
        catch (JournalCorruptException exception)
        {
            EntryList.Children.Add(Text(_localizer.Format("JournalReadErrorFormat", exception.Message), "WarningBrush"));
            return;
        }

        if (entries.Count == 0)
        {
            EntryList.Children.Add(Text(_localizer.Get("JournalEmpty"), "InkSecondaryBrush"));
        }

        foreach (JournalEntry entry in entries)
        {
            EntryList.Children.Add(Row(entry));
        }
    }

    // Same language as the confirm card: a rounded row with an edge that is lit while the item is in the
    // bin and warns when it was deleted for good.
    private Grid Row(JournalEntry entry)
    {
        bool deletedForGood = entry.Undo is DeletedForGood;
        bool inBin = entry.UndoneAt is null && !deletedForGood;
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
        row.Children.Add(new Border { Background = Resource<Brush>(deletedForGood ? "WarningBrush" : inBin ? "AccentBrush" : "LineBrush") });

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
        var message = Text(string.Empty, "WarningBrush", fontSize: 12);
        message.Visibility = Visibility.Collapsed;
        details.Children.Add(message);
        Grid.SetColumn(details, 1);
        row.Children.Add(details);

        if (inBin)
        {
            var undo = new Button
            {
                Content = _localizer.Get("JournalUndoButton"),
                Style = Resource<Style>("VoidGhostButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(18, 8, 18, 8),
            };
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
            Show();
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
