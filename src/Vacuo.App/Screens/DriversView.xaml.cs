using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.Core.Drivers;
using Windows.System;

namespace Vacuo.App.Screens;

/// <summary>
/// The Drivers screen (Aşama 3g dilim 3): the BIOS and the display, network and sound drivers. Only reads.
/// A button opens the browser or the Windows Update settings; Vacuo itself downloads and installs nothing.
/// </summary>
public sealed partial class DriversView : UserControl
{
    private Localizer _localizer = null!;
    private Uri _biosPage = SupportPages.WindowsUpdate;

    public DriversView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal event EventHandler? RefreshRequested;

    internal void Initialize(Localizer localizer)
    {
        _localizer = localizer;
        BackButton.Content = localizer.Get("DiskBackButton");
        ScreenTitle.Text = localizer.Get("DriversTitle");
        RefreshButton.Content = localizer.Get("DriversRefreshButton");
        BiosTitle.Text = localizer.Get("DriversBiosTitle");
        AboutTitle.Text = localizer.Get("DriversAboutTitle");
        AboutText.Text = localizer.Get("DriversAbout");
    }

    internal void ShowMessage(string message)
    {
        RefreshButton.IsEnabled = true;
        FailureText.Text = message;
        FailureText.Visibility = Visibility.Visible;
    }

    internal void Show(BiosInfo? bios, DriverOverview drivers, DateOnly today)
    {
        RefreshButton.IsEnabled = true;
        FailureText.Visibility = Visibility.Collapsed;
        ShowBios(bios);

        ClassCards.Children.Clear();
        foreach (IGrouping<DriverClass, DriverEntry> group in drivers.Shown.GroupBy(driver => driver.Class))
        {
            ClassCards.Children.Add(ClassCard(group.Key, group, today));
        }

        FromWindowsText.Text = _localizer.Format("DriversFromWindowsFormat", drivers.FromWindows);
        FromWindowsText.Visibility = drivers.FromWindows > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowBios(BiosInfo? bios)
    {
        BiosCard.Visibility = bios is null ? Visibility.Collapsed : Visibility.Visible;
        if (bios is null)
        {
            return;
        }

        BiosGrid.Children.Clear();
        BiosGrid.RowDefinitions.Clear();
        AddBiosRow("DriversBiosManufacturer", bios.Manufacturer);
        AddBiosRow("DriversBiosModel", bios.Model);
        AddBiosRow("DriversBiosVendor", bios.Vendor);
        AddBiosRow("DriversBiosVersion", bios.Version);
        AddBiosRow("DriversBiosDate", bios.Date?.ToString("d", _localizer.Culture));

        // The computer's maker publishes its BIOS, not the firmware's vendor.
        Uri? page = SupportPages.For(bios.Manufacturer);
        _biosPage = page ?? SupportPages.WindowsUpdate;
        BiosSupportButton.Content = _localizer.Get(page is null ? "DriversWindowsUpdateButton" : "DriversSupportButton");
    }

    private void AddBiosRow(string labelKey, string? value)
    {
        int row = BiosGrid.RowDefinitions.Count;
        BiosGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock { Text = _localizer.Get(labelKey), Foreground = Brush("InkSecondaryBrush") };
        var text = new TextBlock
        {
            Text = value ?? _localizer.Get("DriversNotKnown"),
            Foreground = Brush("InkBrush"),
            HorizontalTextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(label, row);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        BiosGrid.Children.Add(label);
        BiosGrid.Children.Add(text);
    }

    private Border ClassCard(DriverClass kind, IEnumerable<DriverEntry> drivers, DateOnly today)
    {
        var rows = new StackPanel { Spacing = 12 };
        rows.Children.Add(new TextBlock
        {
            Text = _localizer.Get($"DriversClass_{kind}"),
            Style = (Style)Application.Current.Resources["CardTitleStyle"],
        });
        foreach (DriverEntry driver in drivers)
        {
            rows.Children.Add(DriverRow(driver, today));
        }

        return new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = rows };
    }

    private Grid DriverRow(DriverEntry driver, DateOnly today)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = driver.Name, Foreground = Brush("InkBrush"), TextWrapping = TextWrapping.Wrap });
        texts.Children.Add(new TextBlock { Text = Details(driver), Style = (Style)Application.Current.Resources["CardBodyStyle"] });
        if (DriverAgeRule.LooksOld(driver, today))
        {
            texts.Children.Add(new TextBlock
            {
                Text = _localizer.Get("DriversLooksOld"),
                Foreground = Brush("WarningBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        row.Children.Add(texts);
        Button open = OpenButton(driver);
        Grid.SetColumn(open, 1);
        row.Children.Add(open);
        return row;
    }

    private Button OpenButton(DriverEntry driver)
    {
        Uri? page = SupportPages.For(driver.Provider);
        var open = new Button
        {
            Content = _localizer.Get(page is null ? "DriversWindowsUpdateButton" : "DriversSupportButton"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(open, _localizer.Format(page is null ? "DriversWindowsUpdateNameFormat" : "DriversSupportNameFormat", driver.Name));
        open.Click += async (_, _) => await Launcher.LaunchUriAsync(page ?? SupportPages.WindowsUpdate);
        return open;
    }

    // "NVIDIA · Version 32.0 · 4.09.2026", without what the driver does not say.
    private string Details(DriverEntry driver)
    {
        string? version = driver.Version is null ? null : _localizer.Format("DriversVersionFormat", driver.Version);
        string?[] parts = [driver.Provider, version, driver.Date?.ToString("d", _localizer.Culture)];
        return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnBiosSupportClick(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(_biosPage);
}
