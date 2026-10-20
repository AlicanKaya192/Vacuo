using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Ram;
using Windows.UI;

namespace Vacuo.App.Screens;

/// <summary>
/// The Memory screen: the pressure, why Vacuo does not "clean RAM", and the running apps as idle,
/// in use, and system and protected (B-137). Only reads in this slice; nothing can be closed yet.
/// All grouping lives in <see cref="MemoryScreen"/>.
/// </summary>
public sealed partial class MemoryView : UserControl
{
    private Localizer _localizer = null!;

    public MemoryView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? RefreshRequested;

    internal void Initialize(Localizer localizer)
    {
        _localizer = localizer;
        BackButton.Content = localizer.Get("DiskBackButton");
        ScreenTitle.Text = localizer.Get("MemoryTitle");
        RefreshButton.Content = localizer.Get("MemoryRefreshButton");
        WhyText.Text = localizer.Get("MemoryWhyNoCleaning");
    }

    internal void ShowMessage(string message)
    {
        RefreshButton.IsEnabled = true;
        SectionList.Children.Clear();
        SectionList.Children.Add(Text(message, "WarningBrush"));
    }

    internal void Show(MemoryScreen screen)
    {
        RefreshButton.IsEnabled = true;
        ShowSnapshot(screen.Snapshot);
        HistoryText.Text = screen.WatchingSince is { } since
            ? _localizer.Format("MemoryWatchingSinceFormat", since.ToLocalTime().ToString("t", _localizer.Culture))
            : _localizer.Get("MemoryNoHistory");

        SectionList.Children.Clear();
        foreach (MemorySection section in screen.Sections)
        {
            AddSection(section, screen.HistoryKnown);
        }
    }

    /// <summary>A newer reading of the counters; the app list stays until the next refresh.</summary>
    internal void ShowSnapshot(MemorySnapshot snapshot)
    {
        MemoryPressure pressure = MemoryPressureRule.Of(snapshot);
        PressureText.Text = MemoryTexts.PressureName(_localizer, pressure);
        PressureText.Foreground = MemoryTexts.PressureBrush(pressure);
        PressureHint.Text = MemoryTexts.PressureHint(_localizer, pressure);
        NumbersText.Text = $"{MemoryTexts.Available(_localizer, snapshot)} · {MemoryTexts.Committed(_localizer, snapshot)}";
    }

    private void AddSection(MemorySection section, bool historyKnown)
    {
        var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
        header.Children.Add(new TextBlock
        {
            Text = $"{MemoryTexts.GroupName(_localizer, section.Group)} · {DiskTexts.Size(_localizer, section.TotalBytes)}",
            Style = Resource<Style>("CardTitleStyle"),
        });
        header.Children.Add(Text(MemoryTexts.GroupHint(_localizer, section.Group), "InkSecondaryBrush", fontSize: 12));
        SectionList.Children.Add(header);

        if (section.Shown.Count == 0 && section.OthersCount == 0)
        {
            SectionList.Children.Add(Text(_localizer.Get("MemoryEmptyGroup"), "InkSecondaryBrush", fontSize: 12));
            return;
        }

        foreach (AppUse use in section.Shown)
        {
            SectionList.Children.Add(Row(use, section.Group, historyKnown));
        }

        if (section.OthersCount > 0)
        {
            SectionList.Children.Add(Text(
                _localizer.Format("MemoryOthersFormat", section.OthersCount, DiskTexts.Size(_localizer, section.OthersBytes)),
                "InkSecondaryBrush",
                fontSize: 12));
        }
    }

    // Same rounded row as the Journal; the edge is lit only for idle apps, the ones worth a look.
    private Grid Row(AppUse use, AppGroup group, bool historyKnown)
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
        row.Children.Add(new Border { Background = Resource<Brush>(group == AppGroup.Idle ? "AccentBrush" : "LineBrush") });

        var details = new StackPanel { Padding = new Thickness(0, 8, 0, 8), Spacing = 2 };
        details.Children.Add(Text(use.App.Name, "InkBrush"));
        string usage = MemoryTexts.Usage(_localizer, use, historyKnown);
        if (usage.Length > 0)
        {
            details.Children.Add(Text(usage, "InkSecondaryBrush", fontSize: 12));
        }

        Grid.SetColumn(details, 1);
        row.Children.Add(details);

        TextBlock size = Text(DiskTexts.Size(_localizer, use.App.PrivateBytes), "InkBrush");
        size.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(size, 2);
        row.Children.Add(size);
        return row;
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

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
}
