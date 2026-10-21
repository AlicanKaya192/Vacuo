using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Formatting;
using Vacuo.Core.Journal;

namespace Vacuo.App.Screens;

/// <summary>
/// "Bu hafta neler boşaldı" (Aşama 3d E): the Journal's last seven days, one card per part of Vacuo, turning in
/// perspective like a carousel. The movement is set as implicit transitions, so the compositor draws it and
/// the UI thread only picks the front card every few seconds. It turns only while the screen is in sight and
/// the window is open; with Windows animations off the cards stand still as a plain list (vacuo-kurallar 9).
/// Only reads the Journal.
/// </summary>
public sealed partial class WeekView : UserControl
{
    private const double CardWidth = 280;
    private const float SideOffset = 210;
    private const float SideTiltDegrees = 38;
    private const float SideScale = 0.82f;
    private const double SideOpacity = 0.55;
    private static readonly TimeSpan TurnDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan TurnEvery = TimeSpan.FromSeconds(3.5);

    private readonly List<Border> _cards = [];
    private Localizer _localizer = null!;
    private bool _animate;
    private bool _windowOpen;
    private int _front;
    private DispatcherQueueTimer? _turner;

    public WeekView()
    {
        InitializeComponent();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => UpdateTurning());
    }

    internal event EventHandler? BackRequested;

    internal void Initialize(Localizer localizer, bool animationsEnabled)
    {
        _localizer = localizer;
        _animate = animationsEnabled;
        BackButton.Content = localizer.Get("DiskBackButton");
        ScreenTitle.Text = localizer.Get("WeekTitle");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PreviousButton, localizer.Get("WeekPrevious"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(NextButton, localizer.Get("WeekNext"));
        CarouselArea.Visibility = animationsEnabled ? Visibility.Visible : Visibility.Collapsed;
        ListArea.Visibility = animationsEnabled ? Visibility.Collapsed : Visibility.Visible;
        _turner = DispatcherQueue.CreateTimer();
        _turner.Interval = TurnEvery;
        _turner.Tick += (_, _) => Turn(1);
    }

    /// <summary>Like the other screens: the carousel stops while the panel is hidden.</summary>
    internal void SetWindowOpen(bool open)
    {
        _windowOpen = open;
        UpdateTurning();
    }

    /// <summary>The Journal could not be read; no cards.</summary>
    internal void ShowMessage(string message)
    {
        Show(new WeekSummary([], 0, 0));
        TotalText.Text = message;
    }

    internal void Show(WeekSummary week)
    {
        TotalText.Text = week.Cards.Count == 0
            ? _localizer.Get("WeekEmpty")
            : _localizer.Format("WeekTotalFormat", ByteSizeFormatter.Format(week.InBinBytes, _localizer.Culture));
        DeletedText.Text = _localizer.Format("WeekDeletedFormat", ByteSizeFormatter.Format(week.DeletedBytes, _localizer.Culture));
        DeletedText.Visibility = week.DeletedBytes > 0 ? Visibility.Visible : Visibility.Collapsed;

        Stage.Children.Clear();
        List.Children.Clear();
        _cards.Clear();
        _front = 0;
        foreach (WeekCard card in week.Cards)
        {
            if (_animate)
            {
                Border onStage = CardFor(card);
                onStage.Width = CardWidth;
                PrepareForTurning(onStage);
                _cards.Add(onStage);
                Stage.Children.Add(onStage);
            }
            else
            {
                List.Children.Add(CardFor(card));
            }
        }

        CarouselButtons.Visibility = _cards.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PlaceCards();
        UpdateTurning();
    }

    private Border CardFor(WeekCard card)
    {
        string amount = card.Source == WeekSource.Organize
            ? _localizer.Format("WeekOrganizedFormat", card.Items)
            : ByteSizeFormatter.Format(card.Bytes, _localizer.Culture);
        string last = card.Last.ToLocalTime().ToString("g", _localizer.Culture);
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = _localizer.Get($"WeekSource_{card.Source}"),
            Style = (Style)Application.Current.Resources["CardTitleStyle"],
        });
        content.Children.Add(new TextBlock
        {
            Text = amount,
            FontSize = 26,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
        });
        content.Children.Add(new TextBlock
        {
            Text = _localizer.Format("WeekItemsFormat", card.Items, last),
            Style = (Style)Application.Current.Resources["CardBodyStyle"],
        });
        return new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = content };
    }

    // Turning about its own vertical middle; the compositor animates every change of place from now on.
    private static void PrepareForTurning(Border card)
    {
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.RotationAxis = Vector3.UnitY;
        card.SizeChanged += (_, _) => card.CenterPoint = new Vector3((float)card.ActualWidth / 2, (float)card.ActualHeight / 2, 0);
        card.TranslationTransition = new Vector3Transition { Duration = TurnDuration };
        card.ScaleTransition = new Vector3Transition { Duration = TurnDuration };
        card.RotationTransition = new ScalarTransition { Duration = TurnDuration };
        card.OpacityTransition = new ScalarTransition { Duration = TurnDuration };
    }

    // Each card's place is its distance from the front one around the ring: the front card faces the user,
    // one step away turns aside and shrinks, further ones fade out behind.
    private void PlaceCards()
    {
        int count = _cards.Count;
        for (int i = 0; i < count; i++)
        {
            int step = ((i - _front) % count + count) % count;
            if (step > count / 2)
            {
                step -= count;
            }

            Border card = _cards[i];
            float scale = step == 0 ? 1f : SideScale;
            card.Translation = new Vector3(step * SideOffset, 0, 0);
            card.Rotation = -step * SideTiltDegrees;
            card.Scale = new Vector3(scale, scale, 1);
            card.Opacity = step == 0 ? 1 : Math.Abs(step) == 1 ? SideOpacity : 0;
            Canvas.SetZIndex(card, count - Math.Abs(step));
        }
    }

    private void Turn(int by)
    {
        if (_cards.Count < 2)
        {
            return;
        }

        _front = ((_front + by) % _cards.Count + _cards.Count) % _cards.Count;
        PlaceCards();
    }

    private void UpdateTurning()
    {
        if (_turner is null)
        {
            return;
        }

        bool turning = _animate && _windowOpen && Visibility == Visibility.Visible && _cards.Count > 1;
        if (turning && !_turner.IsRunning)
        {
            _turner.Start();
        }
        else if (!turning)
        {
            _turner.Stop();
        }
    }

    // A button press restarts the wait, so the carousel does not turn again right after it.
    private void OnPreviousClick(object sender, RoutedEventArgs e) => TurnByHand(-1);

    private void OnNextClick(object sender, RoutedEventArgs e) => TurnByHand(1);

    private void TurnByHand(int by)
    {
        Turn(by);
        _turner?.Stop();
        UpdateTurning();
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
