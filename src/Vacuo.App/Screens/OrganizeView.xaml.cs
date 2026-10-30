using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Vacuo.App.Localization;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Journal;
using Vacuo.Core.Organize;
using Vacuo.App.Visuals;
using Vacuo.Windows;
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
    private const double IconSize = 24;

    // The strip never holds the result up for long: if it is rebuilt or stops drawing, the result shows anyway.
    private static readonly TimeSpan FlowWaitAtMost = TimeSpan.FromSeconds(4);

    // The "off" row of the reminder picker; the other rows carry a ScheduleEvery.
    private static readonly object ReminderOff = new();

    private readonly OrganizeScheduleStore _reminders = new(OrganizeScheduleStore.DefaultPath);
    private Localizer _localizer = null!;
    private FolderIcons _icons = null!;
    private FolderOrganizer _organizer = null!;
    private JournalScreen _journal = null!;
    private nint _window;
    private string _folder = DesktopFolder;
    private OrganizeRule _rule = OrganizeRule.ByType;
    private OrganizePlan? _plan;
    private Guid? _lastBatch;
    private bool _busy;
    private bool _animationsEnabled;
    private volatile FlowMap? _flow;
    private volatile bool _flowHidden;
    private IReadOnlyList<FlowTarget> _flowTargets = [];

    // While a run or an undo plays, the strip is not rebuilt under it.
    private bool _flowHeld;

    // The folders and files of the last run, so its undo can send them back along the same lanes.
    private (string Folder, IReadOnlyList<FlowTarget> Targets)? _undoFlow;

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
        _icons = new FolderIcons(new FolderIconWriter(), FolderIcons.InstalledFolder);
        _organizer = new FolderOrganizer(runner, _icons);
        _journal = journal;
        _window = window;
        BackButton.Content = localizer.Get("DiskBackButton");
        ScreenTitle.Text = localizer.Get("OrganizeTitle");
        JournalButton.Content = localizer.Get("DiskJournalButton");
        DesktopButton.Content = localizer.Get("OrganizeFolderDesktop");
        DownloadsButton.Content = localizer.Get("OrganizeFolderDownloads");
        PickButton.Content = localizer.Get("OrganizeFolderPick");
        HintText.Text = localizer.Get("OrganizeHint");
        GamesNote.Text = localizer.Get("OrganizeGamesNote");
        UndoButton.Content = localizer.Get("JournalUndoButton");
        RulePicker.SetItems(Enum.GetValues<OrganizeRule>().Select(rule => (OrganizeTexts.RuleName(localizer, rule), (object)rule)));
        RulePicker.SelectionChanged += OnRuleChanged;
        ReminderNote.Text = localizer.Get("OrganizeReminderNote");
        ReminderPicker.SetItems(
        [
            (localizer.Get("OrganizeReminderOff"), ReminderOff),
            (OrganizeTexts.Every(localizer, ScheduleEvery.Weekly), ScheduleEvery.Weekly),
            (OrganizeTexts.Every(localizer, ScheduleEvery.Monthly), ScheduleEvery.Monthly),
        ]);
        ReminderPicker.SelectionChanged += OnReminderChanged;
        _animationsEnabled = animationsEnabled;
        // The flow only runs while this screen is on view (vacuo-kurallar 9).
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => UpdateFlowRunning());
        FlowCanvas.SizeChanged += (_, _) => BuildFlow();
        Confirm.Initialize(animationsEnabled);
        UpdateRunButton();
    }

    /// <summary>Stops the confirm card's Win2D loop before the window goes away.</summary>
    internal void Shutdown()
    {
        Confirm.Shutdown();
        FlowCanvas.RemoveFromVisualTree();
    }

    /// <summary>Reads the folder again; the screen calls this whenever it comes into view.</summary>
    internal async void Refresh() => await LoadPreviewAsync();

    /// <summary>
    /// How many files of a folder would move under the default rule; the card shows it. Off the UI thread
    /// only for the reading; the names come from the localizer, which wants the UI thread.
    /// </summary>
    internal static async Task<OrganizePlan> PlanAsync(Localizer localizer, string folder, OrganizeRule rule)
    {
        IReadOnlyList<FolderItem> items = await Task.Run(() => FolderItems.Read(folder, new ShellShortcutReader()));
        return OrganizePlanner.Plan(folder, items, rule, target => OrganizeTexts.FolderName(localizer, target), PathProbe.Disk);
    }

    /// <summary>The reminder's notice was clicked: its folder and rule, ready for <see cref="Refresh"/> to preview. Moves nothing.</summary>
    internal void ChooseAskedFolder()
    {
        if (_busy || _reminders.LatestAsked() is not { } asked)
        {
            return;
        }

        _folder = asked.Folder;
        _rule = asked.Rule;
        RulePicker.SelectedIndex = Array.IndexOf(Enum.GetValues<OrganizeRule>(), asked.Rule);
        OutcomeCard.Visibility = Visibility.Collapsed;
        _lastBatch = null;
        _undoFlow = null;
    }

    private void ShowReminders()
    {
        IReadOnlyList<OrganizeSchedule> reminders = _reminders.Load();
        OrganizeSchedule? current = reminders.FirstOrDefault(reminder => IsFolder(reminder.Folder));
        ReminderPicker.SelectedIndex = current is null ? 0 : (int)current.Every + 1;
        ReminderList.Children.Clear();
        ReminderList.Visibility = reminders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (reminders.Count > 0)
        {
            ReminderList.Children.Add(Text(_localizer.Get("OrganizeRemindersTitle"), "InkSecondaryBrush"));
        }

        foreach (OrganizeSchedule reminder in reminders.OrderBy(reminder => LabelOf(reminder.Folder), StringComparer.CurrentCultureIgnoreCase))
        {
            ReminderList.Children.Add(ReminderRow(reminder));
        }
    }

    private Grid ReminderRow(OrganizeSchedule reminder)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string label = LabelOf(reminder.Folder);
        string details = Directory.Exists(reminder.Folder)
            ? $"{label} · {OrganizeTexts.RuleName(_localizer, reminder.Rule)} · {OrganizeTexts.Every(_localizer, reminder.Every)}"
            : $"{label} · {_localizer.Get("OrganizeReminderMissing")}";
        TextBlock text = Text(details, "InkBrush");
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);

        var remove = new Button { Content = _localizer.Get("OrganizeReminderRemove"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(remove, _localizer.Format("OrganizeReminderRemoveNameFormat", label));
        remove.Click += (_, _) => ChangeReminders(() => _reminders.Remove(reminder.Folder));
        Grid.SetColumn(remove, 1);
        row.Children.Add(remove);
        return row;
    }

    // A file that cannot be written only loses the change, and the screen says so.
    private void ChangeReminders(Action change)
    {
        try
        {
            change();
            ShowReminders();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowReminders();
            ReminderList.Visibility = Visibility.Visible;
            ReminderList.Children.Add(Text(_localizer.Format("OrganizeReminderFailedFormat", exception.Message), "WarningBrush"));
        }
    }

    private void OnReminderChanged(object? sender, EventArgs e) => ChangeReminders(() =>
    {
        if (ReminderPicker.SelectedValue is ScheduleEvery every)
        {
            _reminders.Set(_folder, _rule, every, DateTimeOffset.Now);
        }
        else
        {
            _reminders.Remove(_folder);
        }
    });

    private async Task LoadPreviewAsync()
    {
        FolderText.Text = $"{LabelOf(_folder)} · {_folder}";
        ShowReminders();
        GamesNote.Visibility = _rule == OrganizeRule.ByType && IsFolder(DesktopFolder) ? Visibility.Visible : Visibility.Collapsed;
        GroupList.Children.Clear();
        _plan = null;
        BuildFlow();
        UpdateRunButton();
        try
        {
            _plan = await PlanAsync(_localizer, _folder, _rule);
            ShowPlan(_plan);
            BuildFlow();
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

    private string FolderLabel() => LabelOf(_folder);

    private string LabelOf(string folder) =>
        IsSameFolder(folder, DesktopFolder) ? _localizer.Get("OrganizeFolderDesktop")
        : IsSameFolder(folder, DownloadsFolder) ? _localizer.Get("OrganizeFolderDownloads")
        : Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));

    private bool IsFolder(string folder) => IsSameFolder(folder, _folder);

    private static bool IsSameFolder(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private void ShowPlan(OrganizePlan plan)
    {
        if (plan.Moves.Count == 0)
        {
            GroupList.Children.Add(Text(_localizer.Get("OrganizeNothing"), "InkSecondaryBrush"));
            return;
        }

        // Every row keeps the icon column when one row has an icon, so the names stay in line. A contrast
        // theme drops the pictures, as it does the flow (Aşama 3f).
        List<IGrouping<string, PlannedMove>> groups = Groups(plan).ToList();
        bool withIcons = !ContrastMode.IsOn && groups.Any(group => IconFileOf(plan, group.Key) is not null);
        foreach (IGrouping<string, PlannedMove> group in groups)
        {
            bool isNew = plan.FoldersToCreate.Contains(group.Key, StringComparer.OrdinalIgnoreCase);
            GroupList.Children.Add(GroupRow(group, isNew, withIcons, withIcons ? IconFileOf(plan, group.Key) : null));
        }
    }

    // Only the folders Vacuo makes get an icon (B-213, decision 2).
    private string? IconFileOf(OrganizePlan plan, string folder) =>
        plan.FolderTargets?.GetValueOrDefault(folder) is { } target ? _icons.FileFor(target) : null;

    // The frame drawn for this size, from the same file the folder gets; the row's text already says which
    // folder it is, so a screen reader skips the picture.
    private Image FolderIconImage(string iconFile)
    {
        var image = new Image { Width = IconSize, Height = IconSize, VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(image, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _ = LoadIconAsync(image, iconFile);
        return image;
    }

    private async Task LoadIconAsync(Image image, string iconFile)
    {
        try
        {
            int pixels = (int)Math.Ceiling(IconSize * (XamlRoot?.RasterizationScale ?? 1));
            if (IconFrames.Png(await File.ReadAllBytesAsync(iconFile), pixels) is not { } png)
            {
                return;
            }

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(new MemoryStream(png).AsRandomAccessStream());
            image.Source = bitmap;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The row stays without a picture; the folder still gets its icon when it is made.
        }
    }

    // Rebuilt for every plan and size; an empty plan shows no flow.
    private void BuildFlow()
    {
        if (_flowHeld)
        {
            return;
        }

        if (_plan is not { Moves.Count: > 0 } plan || FlowCanvas.ActualWidth <= 0)
        {
            _flow = null;
            FlowCanvas.Visibility = _plan is { Moves.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
            UpdateFlowRunning();
            return;
        }

        // The busiest folders get their own chip; the rest share one, so a big folder stays readable.
        List<IGrouping<string, PlannedMove>> groups = Groups(plan).OrderByDescending(group => group.Count()).ToList();
        List<FlowTarget> targets = groups
            .Take(FlowMap.MaximumTargetsShown)
            .Select(group => FlowTargetOf(
                Path.GetFileName(group.Key),
                group.Select(move => Path.GetFileName(move.Source)).ToList(),
                plan.FoldersToCreate.Contains(group.Key, StringComparer.OrdinalIgnoreCase),
                ContrastMode.IsOn ? null : IconFileOf(plan, group.Key)))
            .ToList();
        List<IGrouping<string, PlannedMove>> rest = groups.Skip(FlowMap.MaximumTargetsShown).ToList();
        if (rest.Count > 0)
        {
            // The shared chip has no lane of names; its stream only says that more goes there.
            int files = rest.Sum(group => group.Count());
            targets.Add(new FlowTarget(string.Empty, _localizer.Format("OrganizeMoreFoldersFormat", rest.Count, files), files, IsNew: false, Files: []));
        }

        double height = FlowMap.HeightFor(targets.Count);
        if (Math.Abs(FlowCanvas.Height - height) > 0.5)
        {
            // The new size comes back through SizeChanged, which builds the flow again. A collapsed strip is
            // never laid out, so it is shown first or that never happens.
            FlowCanvas.Height = height;
            FlowCanvas.Visibility = Visibility.Visible;
            return;
        }

        // The canvas is only drawn; a screen reader reads the same folders and counts from its name.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            FlowCanvas,
            $"{FolderLabel()}: {string.Join(", ", targets.Select(target => target.Label))}");
        ShowFlow(FolderLabel(), targets);
        // In a contrast theme the drawn flow steps aside; the groups below say the same in text (Aşama 3f).
        FlowCanvas.Visibility = ContrastMode.IsOn ? Visibility.Collapsed : Visibility.Visible;
        UpdateFlowRunning();
    }

    private FlowTarget FlowTargetOf(string name, IReadOnlyList<string> files, bool isNew, string? iconFile) =>
        new(name, _localizer.Format("OrganizeGroupFormat", name, files.Count), files.Count, isNew, files, iconFile);

    private FlowMap ShowFlow(string folder, IReadOnlyList<FlowTarget> targets)
    {
        var flow = new FlowMap(
            folder,
            _localizer.Format("JournalFilesFormat", targets.Sum(target => target.Count)),
            targets,
            new System.Numerics.Vector2((float)FlowCanvas.ActualWidth, (float)FlowCanvas.ActualHeight));
        _flow = flow;
        _flowTargets = targets;
        LoadFlowIcons(flow, targets);
        return flow;
    }

    // The chips get the frame drawn for their size from the same file the folder gets; until it loads,
    // or if it cannot, the chip shows its name alone.
    private async void LoadFlowIcons(FlowMap flow, IReadOnlyList<FlowTarget> targets)
    {
        int pixels = (int)Math.Ceiling(FlowMap.IconSize * (XamlRoot?.RasterizationScale ?? 1));
        for (int lane = 0; lane < targets.Count; lane++)
        {
            if (targets[lane].IconFile is not { } iconFile || !FlowCanvas.ReadyToDraw)
            {
                continue;
            }

            try
            {
                if (IconFrames.Png(await File.ReadAllBytesAsync(iconFile), pixels) is { } png)
                {
                    flow.SetIcon(lane, await CanvasBitmap.LoadAsync(FlowCanvas, new MemoryStream(png).AsRandomAccessStream()));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The chip keeps its name alone.
            }
        }
    }

    // A run or an undo plays on the strip only where the strip moves at all.
    private bool FlowCanPlay => _animationsEnabled && !ContrastMode.IsOn && Visibility == Visibility.Visible;

    private static Task Within(Task animation) => Task.WhenAny(animation, Task.Delay(FlowWaitAtMost));

    // The lanes of the last run with only the files that moved, for its undo.
    private (string, IReadOnlyList<FlowTarget>)? UndoFlowOf(string folder, IReadOnlyList<FlowTarget> targets, OrganizeOutcome outcome)
    {
        var failed = outcome.Failed.Select(failure => Path.GetFileName(failure.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<FlowTarget> moved = targets
            .Where(target => target.Files.Count > 0)
            .Select(target => FlowTargetOf(target.Name, target.Files.Where(file => !failed.Contains(file)).ToList(), target.IsNew, target.IconFile))
            .Where(target => target.Count > 0)
            .ToList();
        return moved.Count == 0 ? null : (folder, moved);
    }

    // Undo shows the strip again, even when nothing is left to move, so the files can be seen going back.
    private FlowMap? ShowUndoFlow()
    {
        if (_undoFlow is not { } undo || !FlowCanPlay)
        {
            return null;
        }

        FlowCanvas.Height = FlowMap.HeightFor(undo.Targets.Count);
        FlowCanvas.Visibility = Visibility.Visible;
        FlowCanvas.UpdateLayout();
        if (FlowCanvas.ActualWidth <= 0)
        {
            return null;
        }

        FlowMap flow = ShowFlow(undo.Folder, undo.Targets);
        UpdateFlowRunning();
        return flow;
    }

    // With animation effects off, one still frame is drawn instead of the moving one.
    private void UpdateFlowRunning()
    {
        bool onView = Visibility == Visibility.Visible && _flow is not null;
        FlowCanvas.Paused = !onView || !_animationsEnabled;
        if (onView && !_animationsEnabled)
        {
            FlowCanvas.Invalidate();
        }
    }

    private void OnFlowUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args) =>
        _flow?.Advance(args.Timing.ElapsedTime);

    private void OnFlowDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (!_flowHidden)
        {
            _flow?.Draw(args.DrawingSession);
        }
    }

    private static IEnumerable<IGrouping<string, PlannedMove>> Groups(OrganizePlan plan) =>
        plan.Moves.GroupBy(move => Path.GetDirectoryName(move.Destination)!, StringComparer.OrdinalIgnoreCase);

    // Same rounded row as the other screens; the edge is lit for a folder Vacuo will make, and the icon
    // column shows the icon it will get.
    private Grid GroupRow(IGrouping<string, PlannedMove> group, bool isNew, bool withIcons, string? iconFile)
    {
        var row = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(0, 0, 14, 0),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Resource<Color>("SurfaceColor")),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        row.Children.Add(new Border { Background = Resource<Brush>(isNew ? "AccentBrush" : "LineBrush") });
        if (withIcons)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconSize) });
            if (iconFile is not null)
            {
                Image icon = FolderIconImage(iconFile);
                Grid.SetColumn(icon, 1);
                row.Children.Add(icon);
            }
        }

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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
        Grid.SetColumn(details, row.ColumnDefinitions.Count - 1);
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
        // The hole spins up while the files move; once they have, the strip shows each going to its folder
        // and the result follows when the last has landed (Aşama 3i).
        FlowMap? flow = FlowCanPlay ? _flow : null;
        IReadOnlyList<FlowTarget> played = _flowTargets;
        string folder = FolderLabel();
        _flowHeld = flow is not null;
        flow?.BeginRun();
        try
        {
            OrganizeOutcome outcome = await Task.Run(() => _organizer.Run(plan));
            if (flow is not null)
            {
                await Within(flow.LaunchFiles(outcome.Failed.Select(failure => Path.GetFileName(failure.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase)));
            }

            _lastBatch = outcome.Moved.Count > 0 ? outcome.BatchId : null;
            _undoFlow = _lastBatch is null ? null : UndoFlowOf(folder, played, outcome);
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
            _flowHeld = false;
            Changed?.Invoke(this, EventArgs.Empty);
            await LoadPreviewAsync();
        }
    }

    // The strip's drawing shows through the confirm card's black hole, so it draws nothing while the card is up.
    private async Task<bool> ConfirmAsync(OrganizePlan plan)
    {
        _flowHidden = true;
        FlowCanvas.Invalidate();
        try
        {
            return await AskAsync(plan);
        }
        finally
        {
            _flowHidden = false;
            FlowCanvas.Invalidate();
        }
    }

    private Task<bool> AskAsync(OrganizePlan plan)
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
            // The files come back while the strip shows them leaving their folders for the hole.
            Task<IReadOnlyList<BatchUndoFailure>> undo = Task.Run(() => _journal.UndoBatch(batch));
            // Held before the strip is resized, so the resize does not rebuild it from the current plan.
            _flowHeld = true;
            FlowMap? flow = ShowUndoFlow();
            _flowHeld = flow is not null;
            if (flow is not null)
            {
                await Within(flow.PlayUndo());
            }

            IReadOnlyList<BatchUndoFailure> failures = await undo;
            ShowOutcome([failures.Count == 0
                ? Text(_localizer.Get("OrganizeUndone"), "InkBrush")
                : Text(
                    _localizer.Format("OrganizeUndoFailedFormat", failures.Count, DiskTexts.UndoFailure(_localizer, failures[0].Reason)),
                    "WarningBrush")]);
            if (failures.Count == 0)
            {
                _lastBatch = null;
                _undoFlow = null;
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
            _flowHeld = false;
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
        Announcer.SayLines(ScreenTitle, OutcomePanel);
    }

    private async void ChooseFolder(string folder)
    {
        _folder = folder;
        OutcomeCard.Visibility = Visibility.Collapsed;
        _lastBatch = null;
        _undoFlow = null;
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
            string folder = _folder;
            await LoadPreviewAsync();
            if (_reminders.Load().FirstOrDefault(schedule => IsSameFolder(schedule.Folder, folder)) is { } reminder)
            {
                // The reminder follows the rule on view. After the preview, whose reload would wipe a failure shown here.
                ChangeReminders(() => _reminders.Set(folder, rule, reminder.Every, DateTimeOffset.Now));
            }
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
