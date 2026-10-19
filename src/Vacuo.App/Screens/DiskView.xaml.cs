using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vacuo.App.Localization;
using Vacuo.Core.Disk;
using Vacuo.Core.Scanning;

namespace Vacuo.App.Screens;

/// <summary>
/// The Disk screen: buckets, rows to pick, the selection total and a preview of the move.
/// All decisions live in <see cref="DiskScreen"/>; this view only shows them. In this version
/// the move button opens the preview only; nothing is moved.
/// </summary>
public sealed partial class DiskView : UserControl
{
    private Localizer _localizer = null!;
    private DiskScreen? _screen;

    public DiskView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? RescanRequested;

    internal void Initialize(Localizer localizer)
    {
        _localizer = localizer;
        ScreenTitle.Text = localizer.Get("DiskTitle");
        BackButton.Content = localizer.Get("DiskBackButton");
        RescanButton.Content = localizer.Get("DiskRescanButton");
        MoveButton.Content = localizer.Get("DiskMoveButton");
    }

    internal void ShowScanning() => ShowMessage(_localizer.Get("DiskScanning"));

    internal void ShowMessage(string message)
    {
        _screen = null;
        BucketList.Children.Clear();
        BucketList.Children.Add(BodyText(message));
        UpdateSelectionSummary();
    }

    internal void Show(ScanSummary summary)
    {
        _screen = new DiskScreen(summary);
        BucketList.Children.Clear();
        if (summary.Findings.Count == 0)
        {
            BucketList.Children.Add(BodyText(_localizer.Get("DiskNothingFound")));
        }

        foreach (DiskBucket bucket in _screen.Buckets)
        {
            BucketList.Children.Add(BucketSection(bucket));
        }

        if (summary.SkippedEntries > 0)
        {
            BucketList.Children.Add(BodyText(_localizer.Format("DiskSkippedFormat", summary.SkippedEntries)));
        }

        UpdateSelectionSummary();
    }

    private StackPanel BucketSection(DiskBucket bucket)
    {
        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock
        {
            Text = $"{DiskTexts.BucketName(_localizer, bucket.Bucket)} · {DiskTexts.Size(_localizer, bucket.TotalBytes)}",
            Style = (Style)Application.Current.Resources["CardTitleStyle"],
        });

        foreach (ScanFinding finding in bucket.Items)
        {
            section.Children.Add(Row(finding));
        }

        return section;
    }

    private Grid Row(ScanFinding finding)
    {
        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(4),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceRaisedBrush"],
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new CheckBox
        {
            Tag = finding.Path,
            MinWidth = 0,
            IsEnabled = _screen!.IsSelectable(finding.Path),
        };
        check.Checked += OnRowChecked;
        check.Unchecked += OnRowUnchecked;
        row.Children.Add(check);

        var name = new StackPanel();
        name.Children.Add(new TextBlock
        {
            Text = DiskTexts.RuleName(_localizer, finding.Rule),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["InkBrush"],
        });
        name.Children.Add(BodyText(
            $"{DiskTexts.CostName(_localizer, finding.Rule.Cost)} · {finding.LastWriteTimeUtc.ToLocalTime().ToString("d", _localizer.Culture)} · {finding.Path}"));
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        var size = new TextBlock
        {
            Text = DiskTexts.Size(_localizer, finding.Size.Bytes),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["InkBrush"],
        };
        Grid.SetColumn(size, 2);
        row.Children.Add(size);
        return row;
    }

    private static TextBlock BodyText(string text) =>
        new() { Text = text, Style = (Style)Application.Current.Resources["CardBodyStyle"] };

    private void UpdateSelectionSummary()
    {
        int count = _screen?.SelectedCount ?? 0;
        SelectionSummary.Text = count == 0
            ? _localizer.Get("DiskSelectionEmpty")
            : _localizer.Format("DiskSelectionFormat", count, DiskTexts.Size(_localizer, _screen!.SelectedBytes));
        MoveButton.IsEnabled = count > 0;
    }

    private void OnRowChecked(object sender, RoutedEventArgs e)
    {
        _screen?.Select((string)((CheckBox)sender).Tag);
        UpdateSelectionSummary();
    }

    private void OnRowUnchecked(object sender, RoutedEventArgs e)
    {
        _screen?.Deselect((string)((CheckBox)sender).Tag);
        UpdateSelectionSummary();
    }

    private async void OnMoveClick(object sender, RoutedEventArgs e)
    {
        if (_screen is null)
        {
            return;
        }

        IReadOnlyList<ScanFinding> preview = _screen.Preview();
        var list = new StackPanel { Spacing = 4 };
        if (_screen.SelectedYoursCount > 0)
        {
            TextBlock warning = BodyText(_localizer.Format("DiskPreviewYoursWarningFormat", _screen.SelectedYoursCount));
            warning.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["WarningBrush"];
            warning.Margin = new Thickness(0, 0, 0, 8);
            list.Children.Add(warning);
        }

        list.Children.Add(BodyText(_localizer.Format(
            "DiskPreviewFormat", preview.Count, DiskTexts.Size(_localizer, _screen.SelectedBytes))));
        foreach (ScanFinding finding in preview)
        {
            list.Children.Add(BodyText($"{DiskTexts.Size(_localizer, finding.Size.Bytes)} · {finding.Path}"));
        }

        list.Children.Add(BodyText(_localizer.Get("DiskPreviewRecycleNote")));
        list.Children.Add(BodyText(_localizer.Get("DiskPreviewNotYet")));

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _localizer.Get("DiskPreviewTitle"),
            Content = new ScrollViewer { Content = list, MaxHeight = 360 },
            CloseButtonText = _localizer.Get("DiskPreviewClose"),
        };
        await dialog.ShowAsync();
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnRescanClick(object sender, RoutedEventArgs e) => RescanRequested?.Invoke(this, EventArgs.Empty);
}
