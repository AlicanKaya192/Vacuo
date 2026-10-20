using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Scanning;
using Vacuo.Core.Uninstall;
using Vacuo.Windows;

namespace Vacuo.App.Screens;

/// <summary>
/// The Uninstall screen (Aşama 3d C): installed programs, read only from the registry, each with its own
/// uninstaller one click and one confirmation away. Vacuo never uninstalls by itself; when the
/// uninstaller closes it checks whether the program is gone and only suggests the folders it left in
/// AppData, which go to the Recycle Bin when the user picks and confirms them.
/// </summary>
public sealed partial class UninstallView : UserControl
{
    // A long list built at once froze the Journal; programs come a page at a time.
    private const int ProgramsPerPage = 50;

    private static readonly ProgramSort[] Sorts = [ProgramSort.Name, ProgramSort.Size, ProgramSort.InstalledOn];

    private readonly List<Button> _uninstallButtons = [];
    private Localizer _localizer = null!;
    private ActionRunner _runner = null!;
    private IRecycleBin _recycleBin = null!;
    private IReadOnlyList<InstalledProgram>? _programs;
    private IReadOnlyList<InstalledProgram> _shownList = [];
    private int _shownCount;
    private Button? _moreButton;
    private InstalledProgram? _pending;
    private LeftoversScreen? _leftovers;
    private InstalledProgram? _leftoverProgram;
    private bool _busy;

    public UninstallView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? JournalRequested;

    internal void Initialize(Localizer localizer, ActionRunner runner, IRecycleBin recycleBin, bool animationsEnabled)
    {
        _localizer = localizer;
        _runner = runner;
        _recycleBin = recycleBin;
        ScreenTitle.Text = localizer.Get("UninstallTitle");
        BackButton.Content = localizer.Get("DiskBackButton");
        JournalButton.Content = localizer.Get("DiskJournalButton");
        RefreshButton.Content = localizer.Get("MemoryRefreshButton");
        RecheckButton.Content = localizer.Get("UninstallRecheckButton");
        MoveLeftoversButton.Content = localizer.Get("DiskMoveButton");
        SearchBox.PlaceholderText = localizer.Get("UninstallSearchPlaceholder");
        SortPicker.SetItems(Sorts.Select(sort => (localizer.Get($"UninstallSort_{sort}"), (object)sort)));
        SortPicker.SelectedIndex = 0;
        SortPicker.SelectionChanged += (_, _) => RenderList();
        Confirm.Initialize(animationsEnabled);
    }

    internal void Shutdown() => Confirm.Shutdown();

    /// <summary>Reads the programs the first time the screen opens; Refresh reads them again.</summary>
    internal async void Refresh()
    {
        if (_programs is null)
        {
            await ReadProgramsAsync();
        }
    }

    private async Task ReadProgramsAsync()
    {
        ShowMessage(_localizer.Get("UninstallReading"));
        try
        {
            _programs = await Task.Run(() => InstalledPrograms.From(UninstallRegistry.Read()));
            RenderList();
        }
        catch (Exception exception)
        {
            // Reading only; a failure is shown instead of closing the panel.
            _programs = null;
            ShowMessage(_localizer.Format("UninstallReadFailedFormat", exception.Message));
        }
    }

    private void ShowMessage(string message)
    {
        ClearList();
        ProgramRows.Children.Add(BodyText(message));
    }

    private void ClearList()
    {
        _uninstallButtons.Clear();
        _moreButton = null;
        _shownCount = 0;
        ProgramRows.Children.Clear();
    }

    private void RenderList()
    {
        if (_programs is null)
        {
            return;
        }

        ClearList();
        var sort = (ProgramSort)(SortPicker.SelectedValue ?? ProgramSort.Name);
        _shownList = ProgramList.Show(_programs, SearchBox.Text, sort);
        long knownBytes = _shownList.Sum(program => program.SizeBytes ?? 0);
        ProgramRows.Children.Add(BodyText(_shownList.Count == 0
            ? _localizer.Get("UninstallNothingMatches")
            : _localizer.Format("UninstallCountFormat", _shownList.Count, DiskTexts.Size(_localizer, knownBytes))));
        ShowMorePrograms();
    }

    private void ShowMorePrograms()
    {
        if (_moreButton is not null)
        {
            ProgramRows.Children.Remove(_moreButton);
            _moreButton = null;
        }

        foreach (InstalledProgram program in _shownList.Skip(_shownCount).Take(ProgramsPerPage))
        {
            ProgramRows.Children.Add(ProgramRow(program));
        }

        _shownCount = Math.Min(_shownList.Count, _shownCount + ProgramsPerPage);
        if (_shownCount < _shownList.Count)
        {
            _moreButton = new Button
            {
                Content = _localizer.Format("UninstallMoreFormat", _shownList.Count - _shownCount),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _moreButton.Click += (_, _) => ShowMorePrograms();
            ProgramRows.Children.Add(_moreButton);
        }
    }

    private Grid ProgramRow(InstalledProgram program)
    {
        var row = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(10, 8, 8, 8),
            CornerRadius = new CornerRadius(4),
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock
        {
            Text = program.Name,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["InkBrush"],
        });
        string details = string.Join(" · ", new[]
        {
            program.Publisher,
            program.Version,
            program.InstalledOn?.ToString("d", _localizer.Culture),
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (details.Length > 0)
        {
            name.Children.Add(BodyText(details));
        }

        row.Children.Add(name);

        var size = new TextBlock
        {
            Text = program.SizeBytes is { } bytes ? DiskTexts.Size(_localizer, bytes) : _localizer.Get("UninstallUnknownSize"),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources[program.SizeBytes is null ? "InkSecondaryBrush" : "InkBrush"],
        };
        Grid.SetColumn(size, 1);
        row.Children.Add(size);

        var uninstall = new Button
        {
            Content = _localizer.Get("UninstallButton"),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = !_busy,
        };
        AutomationProperties.SetName(uninstall, _localizer.Format("UninstallConfirmTitleFormat", program.Name));
        uninstall.Click += async (_, _) => await UninstallAsync(program);
        Grid.SetColumn(uninstall, 2);
        row.Children.Add(uninstall);
        _uninstallButtons.Add(uninstall);
        return row;
    }

    private static TextBlock BodyText(string text) =>
        new() { Text = text, Style = (Style)Application.Current.Resources["CardBodyStyle"] };

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BackButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        JournalButton.IsEnabled = !busy;
        RecheckButton.IsEnabled = !busy;
        foreach (Button button in _uninstallButtons)
        {
            button.IsEnabled = !busy;
        }

        UpdateLeftoverSummary();
    }

    // The program's own uninstaller does the work (vacuo-kurallar 2): Vacuo opens it after one confirmation.
    private async Task UninstallAsync(InstalledProgram program)
    {
        if (_busy || !await ConfirmUninstallAsync(program))
        {
            return;
        }

        if (UninstallCommand.Parse(program.UninstallCommand) is not { } command)
        {
            ShowStatus(_localizer.Get("UninstallCommandUnreadable"));
            return;
        }

        _pending = program;
        ShowStatus(_localizer.Format("UninstallRunningFormat", program.Name));
        SetBusy(true);
        try
        {
            switch (await UninstallerLauncher.RunAsync(command))
            {
                case UninstallerExit.Cancelled:
                    ShowStatus(_localizer.Get("UninstallCancelled"));
                    break;
                case UninstallerExit.NotFound:
                    ShowStatus(_localizer.Format("UninstallNotFoundFormat", command.Program));
                    break;
                default:
                    await CheckRemovedAsync(program);
                    break;
            }
        }
        catch (Exception exception)
        {
            ShowStatus(_localizer.Format("UninstallFailedFormat", exception.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private Task<bool> ConfirmUninstallAsync(InstalledProgram program) => Confirm.AskAsync(new ConfirmRequest(
        _localizer.Format("UninstallConfirmTitleFormat", program.Name),
        _localizer.Get("UninstallConfirmSummary"),
        _localizer.Get("UninstallConfirmButton"),
        _localizer.Get("DiskPreviewClose"),
        [
            ConfirmCard.Note(_localizer.Get("UninstallConfirmAdminNote")),
            ConfirmCard.Note(_localizer.Get("UninstallConfirmLeftoversNote")),
        ]));

    // Some uninstallers close at once and carry on in another window, so the registry decides.
    private async Task CheckRemovedAsync(InstalledProgram program)
    {
        _programs = await Task.Run(() => InstalledPrograms.From(UninstallRegistry.Read()));
        RenderList();
        if (ProgramList.IsStillInstalled(program, _programs))
        {
            ShowStatus(_localizer.Format("UninstallStillInstalledFormat", program.Name), canRecheck: true);
            return;
        }

        _pending = null;
        ShowStatus(_localizer.Format("UninstallRemovedFormat", program.Name) + " " + _localizer.Get("UninstallLeftoversSearching"));
        IReadOnlyList<InstalledProgram> stillInstalled = _programs;
        IReadOnlyList<LeftoverFolder> found = await Task.Run(
            () => LeftoverFinder.Find(program, LeftoverFinder.DataRootsForThisUser(), stillInstalled));
        ShowLeftovers(program, found);
    }

    private void ShowStatus(string text, bool canRecheck = false)
    {
        StatusText.Text = text;
        RecheckButton.Visibility = canRecheck ? Visibility.Visible : Visibility.Collapsed;
        _leftovers = null;
        LeftoverList.Children.Clear();
        LeftoverFooter.Visibility = Visibility.Collapsed;
        OutcomeList.Children.Clear();
        StatusCard.Visibility = Visibility.Visible;
    }

    private void ShowLeftovers(InstalledProgram program, IReadOnlyList<LeftoverFolder> found)
    {
        string removed = _localizer.Format("UninstallRemovedFormat", program.Name);
        if (found.Count == 0)
        {
            ShowStatus($"{removed} {_localizer.Get("UninstallNoLeftovers")}");
            return;
        }

        ShowStatus($"{removed} {_localizer.Format("UninstallLeftoversFormat", found.Count, DiskTexts.Size(_localizer, found.Sum(folder => folder.Size.Bytes)))}");
        _leftovers = new LeftoversScreen(found);
        _leftoverProgram = program;
        RenderLeftovers();
    }

    private void RenderLeftovers()
    {
        LeftoverList.Children.Clear();
        if (_leftovers is null)
        {
            return;
        }

        foreach (LeftoverFolder folder in _leftovers.Folders)
        {
            var check = new CheckBox
            {
                Tag = folder.Path,
                IsChecked = _leftovers.IsPicked(folder.Path),
                Content = $"{folder.Path} · {DiskTexts.Size(_localizer, folder.Size.Bytes)}",
            };
            check.Checked += (_, _) => { _leftovers?.Pick(folder.Path); UpdateLeftoverSummary(); };
            check.Unchecked += (_, _) => { _leftovers?.Unpick(folder.Path); UpdateLeftoverSummary(); };
            LeftoverList.Children.Add(check);
        }

        LeftoverFooter.Visibility = _leftovers.Folders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateLeftoverSummary();
    }

    private void UpdateLeftoverSummary()
    {
        int count = _leftovers?.PickedCount ?? 0;
        LeftoverSummary.Text = count == 0
            ? _localizer.Get("UninstallLeftoversNonePicked")
            : _localizer.Format("UninstallLeftoversPreviewFormat", count, DiskTexts.Size(_localizer, _leftovers!.PickedBytes));
        MoveLeftoversButton.IsEnabled = count > 0 && !_busy;
    }

    private async void OnMoveLeftoversClick(object sender, RoutedEventArgs e)
    {
        if (_leftovers is not { PickedCount: > 0 } leftovers || _leftoverProgram is not { } program || !await ConfirmLeftoversAsync(leftovers, program))
        {
            return;
        }

        SetBusy(true);
        OutcomeList.Children.Clear();
        try
        {
            MoveOutcome outcome = await Task.Run(() => leftovers.MoveSelected(_runner, _recycleBin));
            RenderLeftovers();
            ShowOutcome(outcome);
        }
        catch (Exception exception)
        {
            // Unexpected (e.g. the journal could not be written): say so and keep what is left.
            RenderLeftovers();
            OutcomeList.Children.Add(Warning(_localizer.Format("DiskMoveErrorFormat", exception.Message)));
        }
        finally
        {
            SetBusy(false);
        }
    }

    // The folders may hold the program's settings (vacuo-kurallar 12): the warning and the Recycle Bin note always show.
    private Task<bool> ConfirmLeftoversAsync(LeftoversScreen leftovers, InstalledProgram program) => Confirm.AskAsync(new ConfirmRequest(
        _localizer.Get("UninstallLeftoversPreviewTitle"),
        _localizer.Format("UninstallLeftoversPreviewFormat", leftovers.PickedCount, DiskTexts.Size(_localizer, leftovers.PickedBytes)),
        _localizer.Get("DiskConfirmMove"),
        _localizer.Get("DiskPreviewClose"),
        [
            ConfirmCard.Warning(_localizer.Format("UninstallLeftoversWarningFormat", program.Name)),
            ConfirmCard.List(leftovers.Folders.Where(folder => leftovers.IsPicked(folder.Path)).Select(folder => new ConfirmItem(
                DiskRing.ColorOf(Bucket.Yours),
                Path.GetFileName(folder.Path),
                folder.Path,
                DiskTexts.Size(_localizer, folder.Size.Bytes)))),
            ConfirmCard.Note(_localizer.Get("DiskPreviewRecycleNote")),
        ]));

    private void ShowOutcome(MoveOutcome outcome)
    {
        if (outcome.Moved.Count > 0)
        {
            TextBlock moved = BodyText(_localizer.Format("DiskMovedFormat", DiskTexts.Size(_localizer, outcome.FreedBytes)));
            moved.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
            OutcomeList.Children.Add(moved);
        }

        if (outcome.Failed.Count > 0)
        {
            OutcomeList.Children.Add(Warning(_localizer.Format("DiskFailedFormat", outcome.Failed.Count)));
            foreach (MoveFailure failure in outcome.Failed)
            {
                OutcomeList.Children.Add(BodyText($"{DiskTexts.FailureReason(_localizer, failure.Reason)} · {failure.Path}"));
            }
        }
    }

    private static TextBlock Warning(string text)
    {
        TextBlock warning = BodyText(text);
        warning.Foreground = (Brush)Application.Current.Resources["WarningBrush"];
        return warning;
    }

    private async void OnRecheckClick(object sender, RoutedEventArgs e)
    {
        if (_pending is not { } program || _busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await CheckRemovedAsync(program);
        }
        catch (Exception exception)
        {
            ShowStatus(_localizer.Format("UninstallFailedFormat", exception.Message), canRecheck: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RenderList();

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReadProgramsAsync();

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnJournalClick(object sender, RoutedEventArgs e) => JournalRequested?.Invoke(this, EventArgs.Empty);
}
