using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Battery;

namespace Vacuo.App.Screens;

/// <summary>
/// The Battery screen (Aşama 3b): health and its band, the capacities behind it, charge, cycles,
/// power and a time estimate, and what the user can do. Only reads; nothing is changed or stored.
/// </summary>
public sealed partial class BatteryView : UserControl
{
    private Localizer _localizer = null!;

    public BatteryView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? RefreshRequested;

    internal void Initialize(Localizer localizer)
    {
        _localizer = localizer;
        BackButton.Content = localizer.Get("DiskBackButton");
        ScreenTitle.Text = localizer.Get("BatteryTitle");
        RefreshButton.Content = localizer.Get("BatteryRefreshButton");
        CappedNote.Text = localizer.Get("BatteryCappedNote");
        AboutTitle.Text = localizer.Get("BatteryAboutTitle");
        AboutText.Text = localizer.Get("BatteryAbout");
    }

    internal void ShowMessage(string message)
    {
        RefreshButton.IsEnabled = true;
        FailureText.Text = message;
        FailureText.Visibility = Visibility.Visible;
    }

    internal void Show(BatterySummary summary)
    {
        RefreshButton.IsEnabled = true;
        FailureText.Visibility = Visibility.Collapsed;
        BatteryHealth health = summary.Health;
        HealthText.Text = BatteryTexts.Health(_localizer, health);
        HealthText.Foreground = BatteryTexts.BandBrush(health.Band);
        HealthHint.Text = BatteryTexts.BandHint(_localizer, health.Band);
        CappedNote.Visibility = health.CappedAt100 ? Visibility.Visible : Visibility.Collapsed;

        NumbersGrid.Children.Clear();
        NumbersGrid.RowDefinitions.Clear();
        AddRow("BatteryDesign", BatteryTexts.Energy(_localizer, summary.DesignCapacity, summary.RelativeUnits));
        AddRow("BatteryFullCharge", BatteryTexts.Energy(_localizer, summary.FullChargeCapacity, summary.RelativeUnits));
        AddRow("BatteryWear", BatteryTexts.Percent(_localizer, 100 - health.Percent));
        AddRow("BatteryCharge", BatteryTexts.Percent(_localizer, summary.ChargePercent));
        AddRow("BatteryCycles", summary.CycleCount?.ToString(_localizer.Culture) ?? _localizer.Get("BatteryCyclesNotReported"));
        AddRow("BatteryStateLabel", _localizer.Get($"BatteryState_{summary.State}"));
        AddRow("BatteryPower", BatteryTexts.Power(_localizer, summary.RateMilliwatts));
        AddRow("BatteryEstimate", BatteryTexts.Estimate(_localizer, summary) ?? _localizer.Get("BatteryNotKnown"));
    }

    private void AddRow(string labelKey, string value)
    {
        int row = NumbersGrid.RowDefinitions.Count;
        NumbersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock { Text = _localizer.Get(labelKey), Foreground = Brush("InkSecondaryBrush") };
        var number = new TextBlock { Text = value, Foreground = Brush("InkBrush"), HorizontalTextAlignment = TextAlignment.Right };
        Grid.SetRow(label, row);
        Grid.SetRow(number, row);
        Grid.SetColumn(number, 1);
        NumbersGrid.Children.Add(label);
        NumbersGrid.Children.Add(number);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }
}
