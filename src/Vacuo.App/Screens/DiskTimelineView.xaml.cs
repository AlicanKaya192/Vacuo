using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Vacuo.App.Localization;
using Vacuo.Core.Disk;

namespace Vacuo.App.Screens;

/// <summary>
/// The "Why is it full?" screen (Aşama 3l): how the drive changed between the readings of the disk history, and
/// which folders moved. Only reads; nothing can be moved from here.
/// </summary>
public sealed partial class DiskTimelineView : UserControl
{
    private const double BarHeight = 6;

    // A step that only just counts still shows a bar one can see.
    private const double SmallestBarShare = 0.02;

    private Localizer _localizer = null!;

    public DiskTimelineView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal void Initialize(Localizer localizer)
    {
        _localizer = localizer;
        BackButton.Content = localizer.Get("DiskBackButton");
        ScreenTitle.Text = localizer.Get("DiskTimelineTitle");
        NoteTitle.Text = localizer.Get("DiskTimelineNoteTitle");
        NoteText.Text = localizer.Get("DiskTimelineNote");
    }

    internal void Show(DiskTimeline? timeline)
    {
        StepCards.Children.Clear();
        LargestText.Visibility = timeline?.Largest is null ? Visibility.Collapsed : Visibility.Visible;
        if (timeline is null)
        {
            TotalText.Text = _localizer.Get("DiskGrowthStarting");
            return;
        }

        LargestText.Text = DiskTexts.TimelineLargest(_localizer, timeline);
        TotalText.Text = DiskTexts.TimelineTotal(_localizer, timeline);
        long largestChange = timeline.Steps.Max(step => Math.Abs(step.ChangeBytes));
        foreach (DiskTimelineStep step in timeline.Steps)
        {
            StepCards.Children.Add(StepCard(step, largestChange));
        }
    }

    private Border StepCard(DiskTimelineStep step, long largestChange)
    {
        var card = new StackPanel { Spacing = 6 };
        card.Children.Add(Heading(step));
        if (Math.Abs(step.ChangeBytes) >= DiskTimeline.NoticeableBytes)
        {
            card.Children.Add(Bar(step.ChangeBytes, largestChange));
        }

        foreach (string line in DiskTexts.TimelineFolders(_localizer, step))
        {
            card.Children.Add(new TextBlock { Text = line, Style = (Style)Application.Current.Resources["CardBodyStyle"] });
        }

        return new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = card };
    }

    private Grid Heading(DiskTimelineStep step)
    {
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock
        {
            Text = DiskTexts.TimelinePeriod(_localizer, step),
            Foreground = Brush("InkBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        var change = new TextBlock
        {
            Text = DiskTexts.TimelineChange(_localizer, step),
            Foreground = Brush(step.IsNoticeable ? "InkBrush" : "InkSecondaryBrush"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        Grid.SetColumn(change, 1);
        heading.Children.Add(change);
        return heading;
    }

    // Repeats the number beside it for the eye, so a screen reader skips it.
    private static Grid Bar(long changeBytes, long largestChange)
    {
        double share = Math.Max(SmallestBarShare, (double)Math.Abs(changeBytes) / largestChange);
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
        bar.Children.Add(new Rectangle
        {
            Height = BarHeight,
            RadiusX = BarHeight / 2,
            RadiusY = BarHeight / 2,
            Fill = Brush(changeBytes > 0 ? "AccentBrush" : "InkSecondaryBrush"),
        });
        AutomationProperties.SetAccessibilityView(bar, AccessibilityView.Raw);
        return bar;
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
