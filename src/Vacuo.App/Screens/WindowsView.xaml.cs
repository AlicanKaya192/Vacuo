using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Actions;
using Vacuo.Core.Disk;
using Vacuo.Core.Leftovers;
using Vacuo.Core.Scanning;
using Vacuo.Windows;
using Windows.UI;

namespace Vacuo.App.Screens;

/// <summary>
/// The Windows screen (ADR-003): the user's temp folder, moved to the Recycle Bin like on the
/// Disk screen, and the administrator cleanups, deleted for good under one administrator prompt.
/// Decisions live in <see cref="DiskScreen"/> and <see cref="AdminCleanup"/>; nothing starts
/// selected and nothing runs without the confirm card.
/// </summary>
public sealed partial class WindowsView : UserControl
{
    private const int TempPageSize = 60;
    private readonly List<CheckBox> _tempChecks = [];
    private IReadOnlyList<ScanFinding> _tempItems = [];
    private Button? _moreTempButton;
    private readonly List<CheckBox> _adminChecks = [];
    private Localizer _localizer = null!;
    private ActionRunner _runner = null!;
    private IRecycleBin _recycleBin = null!;
    private IElevatedLauncher _launcher = null!;
    private WindowsLocations _locations = null!;
    private DiskScreen? _temp;
    private AdminCleanup? _admin;
    private bool _busy;
    // Ticking rows one by one also moves "Select all"; that must not select or clear everything.
    private bool _syncingSelectAll;
    // "Select all" ticks every row; each row then only marks its item, and the totals are worked out once at
    // the end. Done per row, they sorted the whole selection a thousand times over and the screen froze.
    private bool _settingAllRows;

    public WindowsView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? JournalRequested;

    internal void Initialize(
        Localizer localizer, ActionRunner runner, IRecycleBin recycleBin, IElevatedLauncher launcher, WindowsLocations locations, bool animationsEnabled)
    {
        _localizer = localizer;
        _runner = runner;
        _recycleBin = recycleBin;
        _launcher = launcher;
        _locations = locations;
        Confirm.Initialize(animationsEnabled);
        Stage.Initialize(localizer, animationsEnabled);
        TempProgress.Initialize(animationsEnabled);
        AdminProgress.Initialize(animationsEnabled);
        ScreenTitle.Text = localizer.Get("WindowsTitle");
        BackButton.Content = localizer.Get("DiskBackButton");
        RescanButton.Content = localizer.Get("DiskRescanButton");
        JournalButton.Content = localizer.Get("DiskJournalButton");
        TempHeader.Text = localizer.Get("WindowsTempHeader");
        TempHint.Text = localizer.Get("WindowsTempHint");
        SelectAllBox.Content = localizer.Get("WindowsSelectAll");
        TempMoveButton.Content = localizer.Get("DiskMoveButton");
        AdminHeader.Text = localizer.Get("WindowsAdminHeader");
        AdminHint.Text = localizer.Get("WindowsAdminHint");
        AdminRunButton.Content = localizer.Get("WindowsAdminButton");
        WindowsOldText.Text = localizer.Get("WindowsOldText");
        WindowsOldButton.Content = localizer.Get("WindowsOpenDiskCleanup");
    }

    /// <summary>Stops Win2D's game-loop thread before the window goes away.</summary>
    internal void Shutdown()
    {
        Confirm.Shutdown();
        Stage.Shutdown();
    }

    /// <summary>Scans the temp folder and looks which administrator cleanups apply; only reads.</summary>
    internal async void Refresh()
    {
        if (_busy)
        {
            return;
        }

        Confirm.Cancel();
        _temp = null;
        _admin = null;
        ClearTemp();
        TempResult.Children.Clear();
        AdminResult.Children.Clear();
        TempList.Children.Add(Text(_localizer.Get("DiskScanning"), "InkSecondaryBrush"));
        RenderAdmin();
        UpdateSummaries();
        try
        {
            (ScanSummary summary, AdminCleanup admin) = await Task.Run(() =>
                (ScanSummary.Combine([new UserTempScanner(_locations.UserTemp).Scan()]), new AdminCleanup(_locations)));
            _temp = new DiskScreen(summary);
            _admin = admin;
            RenderTemp();
            RenderAdmin();
        }
        catch (Exception exception)
        {
            ClearTemp();
            TempList.Children.Add(Text(_localizer.Format("DiskScanFailedFormat", exception.Message), "WarningBrush"));
        }

        UpdateSummaries();
    }

    private void ClearTemp()
    {
        _tempChecks.Clear();
        _tempItems = [];
        _moreTempButton = null;
        TempList.Children.Clear();
        _syncingSelectAll = true;
        SelectAllBox.IsChecked = false;
        _syncingSelectAll = false;
    }

    // A temp folder holds hundreds of items; building a row for each at once held the screen back
    // (Alican, 21 October 2026: ~1.3 s with ~700 items). Rows come a page at a time, like the Journal.
    private void RenderTemp()
    {
        ClearTemp();
        _tempItems = TempItems();
        if (_tempItems.Count == 0)
        {
            TempList.Children.Add(Text(_localizer.Get("WindowsTempEmpty"), "InkSecondaryBrush"));
        }

        ShowMoreTemp();
        SyncSelectAll();
    }

    private void ShowMoreTemp()
    {
        if (_moreTempButton is not null)
        {
            TempList.Children.Remove(_moreTempButton);
            _moreTempButton = null;
        }

        foreach (ScanFinding finding in _tempItems.Skip(_tempChecks.Count).Take(TempPageSize))
        {
            TempList.Children.Add(TempRow(finding));
        }

        int hidden = _tempItems.Count - _tempChecks.Count;
        if (hidden > 0)
        {
            _moreTempButton = new Button
            {
                Content = _localizer.Format("WindowsMoreFormat", hidden),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = !_busy,
            };
            _moreTempButton.Click += (_, _) => ShowMoreTemp();
            TempList.Children.Add(_moreTempButton);
        }
    }

    private IReadOnlyList<ScanFinding> TempItems() =>
        _temp?.Buckets.SelectMany(bucket => bucket.Items).ToList() ?? [];

    private Grid TempRow(ScanFinding finding)
    {
        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(4),
            Background = Resource<Brush>("SurfaceRaisedBrush"),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new CheckBox { Tag = finding.Path, MinWidth = 0, IsChecked = _temp!.IsSelected(finding.Path), IsEnabled = !_busy };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            check,
            DiskTexts.RowName(Path.GetFileName(finding.Path), DiskTexts.Size(_localizer, finding.Size.Bytes)));
        check.Checked += OnTempRowChecked;
        check.Unchecked += OnTempRowUnchecked;
        row.Children.Add(check);
        _tempChecks.Add(check);

        var name = new StackPanel();
        name.Children.Add(Text(Path.GetFileName(finding.Path), "InkBrush"));
        name.Children.Add(Text(finding.LastWriteTimeUtc.ToLocalTime().ToString("d", _localizer.Culture), "InkSecondaryBrush", fontSize: 12));
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        TextBlock size = Text(DiskTexts.Size(_localizer, finding.Size.Bytes), "InkBrush");
        size.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(size, 2);
        row.Children.Add(size);
        return row;
    }

    private void RenderAdmin()
    {
        _adminChecks.Clear();
        AdminList.Children.Clear();
        WindowsOldPanel.Visibility = _admin?.WindowsOld is null ? Visibility.Collapsed : Visibility.Visible;
        if (_admin is null)
        {
            return;
        }

        foreach (WindowsLeftover leftover in _admin.Offered)
        {
            var label = new StackPanel();
            label.Children.Add(Text(LeftoverName(leftover), "InkBrush"));
            label.Children.Add(Text(LeftoverDetail(leftover), "InkSecondaryBrush", fontSize: 12));
            var check = new CheckBox
            {
                Tag = leftover.Id,
                Content = label,
                IsChecked = _admin.IsSelected(leftover.Id),
                IsEnabled = !_busy,
            };
            // The content is a panel of two lines, so a screen reader would find no name in it.
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(check, DiskTexts.RowName(LeftoverName(leftover), LeftoverDetail(leftover)));
            check.Checked += (_, _) => { _admin?.Select(leftover.Id); UpdateSummaries(); };
            check.Unchecked += (_, _) => { _admin?.Deselect(leftover.Id); UpdateSummaries(); };
            AdminList.Children.Add(check);
            _adminChecks.Add(check);
        }
    }

    private string LeftoverName(WindowsLeftover leftover) => _localizer.Get(LeftoverKey("Leftover_", leftover));

    // A folder shows where it is; DISM, which has no folder, says what it does.
    private string LeftoverDetail(WindowsLeftover leftover) =>
        leftover.Path ?? _localizer.Get(LeftoverKey("LeftoverHint_", leftover));

    // Resource names avoid '-', so "windows-temp" becomes "Leftover_windows_temp".
    private static string LeftoverKey(string prefix, WindowsLeftover leftover) => prefix + leftover.Id.Replace('-', '_');

    private void UpdateSummaries()
    {
        int tempCount = _temp?.SelectedCount ?? 0;
        TempSummary.Text = tempCount == 0
            ? _localizer.Get("DiskSelectionEmpty")
            : _localizer.Format("DiskSelectionFormat", tempCount, DiskTexts.Size(_localizer, _temp!.SelectedBytes));
        TempMoveButton.IsEnabled = tempCount > 0 && !_busy;

        int adminCount = _admin?.SelectedCount ?? 0;
        AdminSummary.Text = adminCount == 0
            ? _localizer.Get("WindowsAdminSelectionEmpty")
            : _localizer.Format("WindowsAdminSelectionFormat", adminCount);
        AdminRunButton.IsEnabled = adminCount > 0 && !_busy;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        BackButton.IsEnabled = !busy;
        RescanButton.IsEnabled = !busy;
        JournalButton.IsEnabled = !busy;
        SelectAllBox.IsEnabled = !busy;
        foreach (CheckBox check in _tempChecks.Concat(_adminChecks))
        {
            check.IsEnabled = !busy;
        }

        if (_moreTempButton is not null)
        {
            _moreTempButton.IsEnabled = !busy;
        }

        UpdateSummaries();
        if (status is not null)
        {
            AdminSummary.Text = status;
        }
    }

    private void OnTempRowChecked(object sender, RoutedEventArgs e)
    {
        _temp?.Select((string)((CheckBox)sender).Tag);
        AfterRowChanged();
    }

    private void OnTempRowUnchecked(object sender, RoutedEventArgs e)
    {
        _temp?.Deselect((string)((CheckBox)sender).Tag);
        AfterRowChanged();
    }

    private void AfterRowChanged()
    {
        if (_settingAllRows)
        {
            return;
        }

        SyncSelectAll();
        UpdateSummaries();
    }

    // From the selection itself, so rows not shown yet count too.
    private void SyncSelectAll()
    {
        int selectable = _tempItems.Count(IsSelectable);
        _syncingSelectAll = true;
        SelectAllBox.IsChecked = selectable > 0 && _temp?.SelectedCount == selectable;
        _syncingSelectAll = false;
    }

    private static bool IsSelectable(ScanFinding finding) => finding.Rule.Bucket != Bucket.Keep;

    private void OnSelectAllChecked(object sender, RoutedEventArgs e) => SetAllTempChecks(true);

    private void OnSelectAllUnchecked(object sender, RoutedEventArgs e) => SetAllTempChecks(false);

    private void SetAllTempChecks(bool isChecked)
    {
        if (_syncingSelectAll || _temp is null)
        {
            return;
        }

        // Every item, the ones on pages not shown yet too; then the rows on screen follow.
        foreach (ScanFinding finding in _tempItems.Where(IsSelectable))
        {
            if (isChecked)
            {
                _temp.Select(finding.Path);
            }
            else
            {
                _temp.Deselect(finding.Path);
            }
        }

        _settingAllRows = true;
        try
        {
            foreach (CheckBox check in _tempChecks)
            {
                check.IsChecked = isChecked;
            }
        }
        finally
        {
            _settingAllRows = false;
        }

        SyncSelectAll();
        UpdateSummaries();
    }

    private async void OnTempMoveClick(object sender, RoutedEventArgs e)
    {
        if (_temp is not { SelectedCount: > 0 } temp || !await Confirm.AskAsync(TempRequest(temp)))
        {
            return;
        }

        TempResult.Children.Clear();
        SetBusy(true);
        TempSummary.Text = _localizer.Get("DiskMoving");
        TempProgress.ShowFraction(0);
        // The black hole eats a planet for every seventh of the work (Aşama 3j); the bar keeps going underneath.
        bool staged = Stage.Begin(_localizer.Get("DiskMoving"));
        // Reported on the UI thread after every item: the bar fills and the line says how many are left.
        var progress = new Progress<MoveProgress>(report =>
        {
            TempProgress.ShowFraction(report.Fraction);
            TempSummary.Text = RemovalTexts.Progress(_localizer, report);
            if (staged)
            {
                Stage.Report(report);
            }
        });
        try
        {
            MoveOutcome outcome = await Task.Run(() => temp.MoveSelected(_runner, _recycleBin, progress));
            if (staged)
            {
                await Stage.FinishAsync();
            }

            RenderTemp();
            ShowTempOutcome(outcome);
        }
        catch (Exception exception)
        {
            // Unexpected (e.g. the journal could not be written): say so and show what is left.
            RenderTemp();
            TempResult.Children.Add(Text(_localizer.Format("DiskMoveErrorFormat", exception.Message), "WarningBrush"));
            Announcer.SayLines(ScreenTitle, TempResult);
        }
        finally
        {
            // After an error the stage closes at once instead of eating the rest as if all went well.
            Stage.Close();
            TempProgress.Hide();
            SetBusy(false);
        }
    }

    private ConfirmRequest TempRequest(DiskScreen temp) => new(
        _localizer.Get("DiskPreviewTitle"),
        _localizer.Format("DiskPreviewFormat", temp.SelectedCount, DiskTexts.Size(_localizer, temp.SelectedBytes)),
        _localizer.Get("DiskConfirmMove"),
        _localizer.Get("DiskPreviewClose"),
        [
            ConfirmCard.List(temp.Preview().Select(finding => new ConfirmItem(
                DiskRing.ColorOf(finding.Rule.Bucket),
                Path.GetFileName(finding.Path),
                finding.Path,
                DiskTexts.Size(_localizer, finding.Size.Bytes)))),
            ConfirmCard.Note(_localizer.Get("DiskPreviewRecycleNote")),
        ]);

    // Temp files in use are common, so failures are counted per reason instead of listed one by one.
    private void ShowTempOutcome(MoveOutcome outcome)
    {
        if (outcome.Moved.Count > 0)
        {
            TempResult.Children.Add(Text(
                _localizer.Format("DiskMovedFormat", DiskTexts.Size(_localizer, outcome.FreedBytes)), "AccentBrush"));
        }

        if (DiskTexts.Largest(_localizer, outcome) is { } largest)
        {
            TempResult.Children.Add(Text(largest, "InkSecondaryBrush", fontSize: 12));
        }

        if (outcome.Failed.Count > 0)
        {
            TempResult.Children.Add(Text(_localizer.Format("DiskFailedFormat", outcome.Failed.Count), "WarningBrush", fontSize: 12));
            foreach (IGrouping<MoveFailureReason, MoveFailure> reason in outcome.Failed.GroupBy(failure => failure.Reason))
            {
                TempResult.Children.Add(Text(
                    $"{DiskTexts.FailureReason(_localizer, reason.Key)} · {reason.Count()}", "InkSecondaryBrush", fontSize: 12));
            }
        }

        Announcer.Say(ScreenTitle, DiskTexts.Outcome(_localizer, outcome));
    }

    private async void OnAdminRunClick(object sender, RoutedEventArgs e)
    {
        if (_admin is not { SelectedCount: > 0 } admin || !await Confirm.AskAsync(AdminRequest(admin)))
        {
            return;
        }

        AdminResult.Children.Clear();
        SetBusy(true, _localizer.Get("WindowsAdminRunning"));
        // The administrator helper reports only when it is done, so the bar sweeps instead of filling.
        AdminProgress.ShowSweep();
        try
        {
            IReadOnlyList<ElevatedResult> results = await Task.Run(() => admin.Run(_launcher, _runner));
            RenderAdmin();
            ShowAdminResults(results);
        }
        catch (ElevationDeclinedException)
        {
            AdminResult.Children.Add(Text(_localizer.Get("WindowsAdminDeclined"), "InkSecondaryBrush", fontSize: 12));
            Announcer.SayLines(ScreenTitle, AdminResult);
        }
        catch (Exception exception)
        {
            AdminResult.Children.Add(Text(_localizer.Format("WindowsAdminErrorFormat", exception.Message), "WarningBrush", fontSize: 12));
            Announcer.SayLines(ScreenTitle, AdminResult);
        }
        finally
        {
            AdminProgress.Hide();
            SetBusy(false);
        }
    }

    private ConfirmRequest AdminRequest(AdminCleanup admin)
    {
        List<WindowsLeftover> chosen = admin.Offered.Where(item => admin.IsSelected(item.Id)).ToList();
        return new ConfirmRequest(
            _localizer.Get("WindowsAdminConfirmTitle"),
            _localizer.Format("WindowsAdminConfirmFormat", chosen.Count),
            _localizer.Get("WindowsAdminConfirm"),
            _localizer.Get("DiskPreviewClose"),
            [
                ConfirmCard.Warning(_localizer.Get("WindowsAdminWarning")),
                // Sizes are not known beforehand: only an administrator can read these folders.
                ConfirmCard.List(chosen.Select(item => new ConfirmItem(
                    Resource<Color>("WarningColor"), LeftoverName(item), LeftoverDetail(item), Size: null))),
                ConfirmCard.Note(_localizer.Get("WindowsAdminNote")),
            ]);
    }

    private void ShowAdminResults(IReadOnlyList<ElevatedResult> results)
    {
        foreach (ElevatedResult result in results)
        {
            WindowsLeftover leftover = WindowsLeftovers.For(_locations).Single(item => item.Id == result.Operation);
            string name = LeftoverName(leftover);
            if (!result.Succeeded)
            {
                string why = result.Error ?? _localizer.Format("WindowsResultExitCodeFormat", result.ExitCode ?? -1);
                AdminResult.Children.Add(Text(_localizer.Format("WindowsResultFailedFormat", name, why), "WarningBrush", fontSize: 12));
                continue;
            }

            string done = leftover.Kind == LeftoverKind.ComponentCleanup
                ? _localizer.Format("WindowsResultDoneFormat", name)
                : _localizer.Format("WindowsResultFolderFormat", name, DiskTexts.Size(_localizer, result.DeletedBytes), result.DeletedFiles);
            if (result.Skipped > 0)
            {
                done += " " + _localizer.Format("WindowsResultSkippedFormat", result.Skipped);
            }

            AdminResult.Children.Add(Text(done, "InkBrush", fontSize: 12));
        }

        Announcer.SayLines(ScreenTitle, AdminResult);
    }

    private void OnOpenDiskCleanupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            DiskCleanupTool.Open(_locations);
        }
        catch (Exception exception)
        {
            AdminResult.Children.Add(Text(_localizer.Format("WindowsAdminErrorFormat", exception.Message), "WarningBrush", fontSize: 12));
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

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnRescanClick(object sender, RoutedEventArgs e) => Refresh();

    private void OnJournalClick(object sender, RoutedEventArgs e) => JournalRequested?.Invoke(this, EventArgs.Empty);
}
