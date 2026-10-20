using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;
using Vacuo.Core.Organize;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace Vacuo.App.Screens;

/// <summary>
/// The Düzen screen (B-161): the Desktop, Downloads or any chosen folder, a rule, and a preview of which
/// file goes into which subfolder before anything moves. A run is one batch in the Journal and can be
/// undone here or there with one click. Planning and moving live in <see cref="Vacuo.Core.Organize"/>.
/// </summary>
public sealed partial class OrganizeView : UserControl
{
    // A group shows this many names; the rest are counted, so a big Downloads folder stays readable.
    private const int NamesPerGroup = 6;

    private Localizer _localizer = null!;
    private FolderOrganizer _organizer = null!;
    private JournalScreen _journal = null!;
    private nint _window;
    private string _folder = DesktopFolder;
    private OrganizeRule _rule = OrganizeRule.ByType;
    private OrganizePlan? _plan;
    private Guid? _lastBatch;
    private bool _busy;

    public OrganizeView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? JournalRequested;

    /// <summary>Raised after files moved or came back, so the card can count again.</summary>
    internal event EventHandler? Changed;

    internal static string DesktopFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    private static string DownloadsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    internal void Initialize(Localizer localizer, ActionRunner runner, JournalScreen journal, nint window, bool animationsEnabled)
    {
        _localizer = localizer;
        _organizer = new FolderOrganizer(runner);
        _journal = journal;
        _window = window;
        BackButton.Content = localizer.Get("DiskBackButton");
        ScreenTitle.Text = localizer.Get("OrganizeTitle");
        JournalButton.Content = localizer.Get("DiskJournalButton");
        DesktopButton.Content = localizer.Get("OrganizeFolderDesktop");
        DownloadsButton.Content = localizer.Get("OrganizeFolderDownloads");
        PickButton.Content = localizer.Get("OrganizeFolderPick");
        HintText.Text = localizer.Get("OrganizeHint");
        UndoButton.Content = localizer.Get("JournalUndoButton");
        RulePicker.SetItems(Enum.GetValues<OrganizeRule>().Select(rule => (OrganizeTexts.RuleName(localizer, rule), (object)rule)));
        RulePicker.SelectionChanged += OnRuleChanged;
        Confirm.Initialize(animationsEnabled);
        UpdateRunButton();
    }

    /// <summary>Stops the confirm card's Win2D loop before the window goes away.</summary>
    internal void Shutdown() => Confirm.Shutdown();

    /// <summary>Reads the folder again; the screen calls this whenever it comes into view.</summary>
    internal async void Refresh() => await LoadPreviewAsync();

    /// <summary>
    /// How many files of a folder would move under the default rule; the card shows it. Off the UI thread
    /// only for the reading; the names come from the localizer, which wants the UI thread.
    /// </summary>
    internal static async Task<OrganizePlan> PlanAsync(Localizer localizer, string folder, OrganizeRule rule)
    {
        IReadOnlyList<FolderItem> items = await Task.Run(() => FolderItems.Read(folder));
        return OrganizePlanner.Plan(folder, items, rule, target => OrganizeTexts.FolderName(localizer, target), PathProbe.Disk);
    }

    private async Task LoadPreviewAsync()
    {
        FolderText.Text = $"{FolderLabel()} · {_folder}";
        GroupList.Children.Clear();
        _plan = null;
        UpdateRunButton();
        try
        {
            _plan = await PlanAsync(_localizer, _folder, _rule);
            ShowPlan(_plan);
        }
        catch (ArgumentException)
        {
            GroupList.Children.Add(Text(_localizer.Get("OrganizeSystemFolder"), "WarningBrush"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GroupList.Children.Add(Text(_localizer.Format("OrganizeReadFailedFormat", exception.Message), "WarningBrush"));
        }

        UpdateRunButton();
    }

    private string FolderLabel() =>
        IsFolder(DesktopFolder) ? _localizer.Get("OrganizeFolderDesktop")
        : IsFolder(DownloadsFolder) ? _localizer.Get("OrganizeFolderDownloads")
        : Path.GetFileName(Path.TrimEndingDirectorySeparator(_folder));

    private bool IsFolder(string folder) => string.Equals(
        Path.TrimEndingDirectorySeparator(folder), Path.TrimEndingDirectorySeparator(_folder), StringComparison.OrdinalIgnoreCase);

    private void ShowPlan(OrganizePlan plan)
    {
        if (plan.Moves.Count == 0)
        {
            GroupList.Children.Add(Text(_localizer.Get("OrganizeNothing"), "InkSecondaryBrush"));
            return;
        }

        foreach (IGrouping<string, PlannedMove> group in Groups(plan))
        {
            GroupList.Children.Add(GroupRow(group, isNew: plan.FoldersToCreate.Contains(group.Key, StringComparer.OrdinalIgnoreCase)));
        }
    }

    private static IEnumerable<IGrouping<string, PlannedMove>> Groups(OrganizePlan plan) =>
        plan.Moves.GroupBy(move => Path.GetDirectoryName(move.Destination)!, StringComparer.OrdinalIgnoreCase);

    // Same rounded row as the other screens; the edge is lit for a folder Vacuo will make.
    private Grid GroupRow(IGrouping<string, PlannedMove> group, bool isNew)
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
        row.Children.Add(new Border { Background = Resource<Brush>(isNew ? "AccentBrush" : "LineBrush") });

        List<PlannedMove> moves = group.ToList();
        string title = _localizer.Format("OrganizeGroupFormat", Path.GetFileName(group.Key), moves.Count);
        var details = new StackPanel { Padding = new Thickness(0, 10, 0, 10), Spacing = 2 };
        details.Children.Add(Text(isNew ? $"{title} · {_localizer.Get("OrganizeNewFolder")}" : title, "InkBrush"));
        string names = string.Join(", ", moves.Take(NamesPerGroup).Select(move => Path.GetFileName(move.Source)));
        if (moves.Count > NamesPerGroup)
        {
            names += " " + _localizer.Format("OrganizeMoreFormat", moves.Count - NamesPerGroup);
        }

        details.Children.Add(Text(names, "InkSecondaryBrush", fontSize: 12));
        Grid.SetColumn(details, 1);
        row.Children.Add(details);
        return row;
    }

    private void UpdateRunButton()
    {
        int count = _plan?.Moves.Count ?? 0;
        RunButton.Content = count == 0 ? _localizer.Get("OrganizeRunButton") : _localizer.Format("OrganizeRunButtonFormat", count);
        RunButton.IsEnabled = count > 0 && !_busy;
        foreach (Button button in new[] { DesktopButton, DownloadsButton, PickButton })
        {
            button.IsEnabled = !_busy;
        }

        RulePicker.IsEnabled = !_busy;
    }

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        if (_plan is not { Moves.Count: > 0 } plan || !await ConfirmAsync(plan))
        {
            return;
        }

        _busy = true;
        UpdateRunButton();
        UndoButton.Visibility = Visibility.Collapsed;
        ShowOutcome([Text(_localizer.Format("OrganizeMovingFormat", plan.Moves.Count), "InkSecondaryBrush")]);
        try
        {
            OrganizeOutcome outcome = await Task.Run(() => _organizer.Run(plan));
            _lastBatch = outcome.Moved.Count > 0 ? outcome.BatchId : null;
            ShowOutcome(OutcomeLines(outcome));
            UndoButton.Visibility = _lastBatch is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception exception)
        {
            // Unexpected (e.g. the journal could not be written): shown here, never a crash.
            ShowOutcome([Text(_localizer.Format("OrganizeErrorFormat", exception.Message), "WarningBrush")]);
        }
        finally
        {
            _busy = false;
            Changed?.Invoke(this, EventArgs.Empty);
            await LoadPreviewAsync();
        }
    }

    private Task<bool> ConfirmAsync(OrganizePlan plan)
    {
        List<IGrouping<string, PlannedMove>> groups = Groups(plan).ToList();
        var items = groups.Select(group => new ConfirmItem(
            Resource<Color>(plan.FoldersToCreate.Contains(group.Key, StringComparer.OrdinalIgnoreCase) ? "AccentColor" : "LineColor"),
            Path.GetFileName(group.Key),
            _localizer.Format("JournalFilesFormat", group.Count()),
            Size: null));
        return Confirm.AskAsync(new ConfirmRequest(
            _localizer.Get("OrganizeConfirmTitle"),
            _localizer.Format("OrganizeConfirmSummaryFormat", plan.Moves.Count, groups.Count),
            _localizer.Get("DiskConfirmMove"),
            _localizer.Get("DiskPreviewClose"),
            [ConfirmCard.List(items), ConfirmCard.Note(_localizer.Get("OrganizeConfirmNote"))]));
    }

    private List<UIElement> OutcomeLines(OrganizeOutcome outcome)
    {
        var lines = new List<UIElement> { Text(_localizer.Format("OrganizeDoneFormat", outcome.Moved.Count), "InkBrush") };
        if (outcome.Failed.Count > 0)
        {
            lines.Add(Text(_localizer.Format("DiskFailedFormat", outcome.Failed.Count), "WarningBrush"));
            foreach (MoveFailure failure in outcome.Failed)
            {
                lines.Add(Text(
                    $"{Path.GetFileName(failure.Path)}: {DiskTexts.FailureReason(_localizer, failure.Reason)}",
                    "InkSecondaryBrush",
                    fontSize: 12));
            }
        }

        return lines;
    }

    private async void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (_lastBatch is not { } batch)
        {
            return;
        }

        _busy = true;
        UndoButton.IsEnabled = false;
        UpdateRunButton();
        try
        {
            IReadOnlyList<BatchUndoFailure> failures = await Task.Run(() => _journal.UndoBatch(batch));
            ShowOutcome([failures.Count == 0
                ? Text(_localizer.Get("OrganizeUndone"), "InkBrush")
                : Text(
                    _localizer.Format("OrganizeUndoFailedFormat", failures.Count, DiskTexts.UndoFailure(_localizer, failures[0].Reason)),
                    "WarningBrush")]);
            if (failures.Count == 0)
            {
                _lastBatch = null;
                UndoButton.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception exception)
        {
            ShowOutcome([Text(_localizer.Format("OrganizeErrorFormat", exception.Message), "WarningBrush")]);
        }
        finally
        {
            _busy = false;
            UndoButton.IsEnabled = true;
            Changed?.Invoke(this, EventArgs.Empty);
            await LoadPreviewAsync();
        }
    }

    private void ShowOutcome(IEnumerable<UIElement> lines)
    {
        OutcomePanel.Children.Clear();
        foreach (UIElement line in lines)
        {
            OutcomePanel.Children.Add(line);
        }

        OutcomeCard.Visibility = Visibility.Visible;
    }

    private async void ChooseFolder(string folder)
    {
        _folder = folder;
        OutcomeCard.Visibility = Visibility.Collapsed;
        _lastBatch = null;
        await LoadPreviewAsync();
    }

    private void OnDesktopClick(object sender, RoutedEventArgs e) => ChooseFolder(DesktopFolder);

    private void OnDownloadsClick(object sender, RoutedEventArgs e) => ChooseFolder(DownloadsFolder);

    // The system folder picker; an unpackaged app has to tell it which window it belongs to.
    private async void OnPickClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Desktop };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _window);
        if (await picker.PickSingleFolderAsync() is StorageFolder picked)
        {
            ChooseFolder(picked.Path);
        }
    }

    private async void OnRuleChanged(object? sender, EventArgs e)
    {
        if (RulePicker.SelectedValue is OrganizeRule rule && rule != _rule)
        {
            _rule = rule;
            await LoadPreviewAsync();
        }
    }

    private static TextBlock Text(string text, string brushKey, double fontSize = 14) => new()
    {
        Text = text,
        FontSize = fontSize,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Resource<Brush>(brushKey),
    };

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        Confirm.Cancel();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnJournalClick(object sender, RoutedEventArgs e) => JournalRequested?.Invoke(this, EventArgs.Empty);
}
