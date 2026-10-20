using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Localization;
using Vacuo.App.Updates;
using Vacuo.Core.Settings;
using Windows.System;

namespace Vacuo.App.Screens;

/// <summary>
/// The Settings screen (Aşama 3c): the language, starting with Windows and updates, then the info pages (about,
/// privacy, licenses, terms) read in place. Each change is saved at once, and a new language shows at once.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    private static readonly LanguageChoice[] Languages = Enum.GetValues<LanguageChoice>();

    // Each page's texts are the resources Info_<page>_Title and Info_<page>_Body.
    private const string AboutPage = "About";
    private static readonly string[] InfoPages = [AboutPage, "Privacy", "License", "Terms"];
    private static readonly string[] ParagraphBreak = ["\r\n\r\n", "\n\n"];
    private const string HeadingMark = "# ";
    private const string ChevronGlyph = "";
    private static readonly Uri RepositoryAddress = new("https://github.com/AlicanKaya192/vacuo");

    private Localizer _localizer = null!;
    private SettingsStore _store = null!;
    private StartupRegistration? _startup;
    private UpdateSession _updates = null!;

    public SettingsView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    /// <summary>Raised after a new language is saved; the panel switches to it at once.</summary>
    internal event EventHandler<LanguageChoice>? LanguageChanged;

    /// <summary>Update was pressed on the Updates card; the panel opens the update screen.</summary>
    internal event EventHandler? UpdateRequested;

    /// <param name="startup">Null when the tray's program is unknown (the panel runs without the tray): the card is hidden.</param>
    internal void Initialize(Localizer localizer, SettingsStore store, StartupRegistration? startup, UpdateSession updates)
    {
        _localizer = localizer;
        _store = store;
        _startup = startup;
        _updates = updates;
        UpdatesTitle.Text = localizer.Get("SettingsUpdatesTitle");
        UpdatesHint.Text = localizer.Get("SettingsUpdatesHint");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(UpdatesBox, UpdatesTitle.Text);
        CheckNowButton.Content = localizer.Get("SettingsUpdatesCheckButton");
        UpdateNowButton.Content = localizer.Get("UpdateStartButton");
        updates.Changed += OnUpdatesChanged;
        StartupCard.Visibility = startup is null ? Visibility.Collapsed : Visibility.Visible;
        StartupTitle.Text = localizer.Get("SettingsStartup");
        StartupHint.Text = localizer.Get("SettingsStartupHint");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(StartupBox, StartupTitle.Text);
        BackButton.Content = localizer.Get("DiskBackButton");
        ScreenTitle.Text = localizer.Get("SettingsTitle");
        LanguageTitle.Text = localizer.Get("SettingsLanguage");
        LanguageHint.Text = localizer.Get("SettingsLanguageHint");
        LanguagePicker.SetItems(Languages.Select(choice => (localizer.Get($"Language_{choice}"), (object)choice)));
        LanguagePicker.SelectionChanged += OnLanguageChanged;
        BuildInfoRows();
    }

    /// <summary>Shows what is saved now; the file may have changed since the screen was made.</summary>
    internal void Refresh()
    {
        ShowSettingsPage();
        LanguagePicker.SelectedIndex = Array.IndexOf(Languages, _store.Load().Language);
        StartupNote.Visibility = Visibility.Collapsed;
        if (_startup is not null)
        {
            StartupBox.IsChecked = _startup.IsOn;
        }

        ShowUpdates();
    }

    internal void Shutdown() => _updates.Changed -= OnUpdatesChanged;

    private void OnUpdatesChanged(object? sender, EventArgs e) => ShowUpdates();

    private void ShowUpdates()
    {
        bool enabled = _updates.CheckingEnabled;
        UpdatesBox.IsChecked = enabled;
        UpdatesState.Text = UpdateTexts.CheckState(_localizer, _updates);
        CheckNowButton.IsEnabled = enabled && _updates.Phase == UpdatePhase.Idle;
        UpdateNowButton.Visibility = _updates.Offer is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnUpdatesBoxClick(object sender, RoutedEventArgs e) => _updates.SetCheckingEnabled(UpdatesBox.IsChecked == true);

    private async void OnCheckNowClick(object sender, RoutedEventArgs e) => await _updates.CheckAsync(byHand: true);

    private void OnUpdateNowClick(object sender, RoutedEventArgs e) => UpdateRequested?.Invoke(this, EventArgs.Empty);

    // The Run key is the truth: after a change, or a refused one, the box shows what it now says.
    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        if (_startup is null)
        {
            return;
        }

        try
        {
            if (StartupBox.IsChecked == true)
            {
                _startup.TurnOn();
            }
            else
            {
                _startup.TurnOff();
            }

            StartupNote.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            StartupNote.Text = _localizer.Get("SettingsStartupFailed");
            StartupNote.Visibility = Visibility.Visible;
        }

        StartupBox.IsChecked = _startup.IsOn;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (LanguagePicker.SelectedValue is not LanguageChoice choice)
        {
            return;
        }

        if (choice == _store.Load().Language)
        {
            return;
        }

        _store.Save(_store.Load() with { Language = choice });
        LanguageChanged?.Invoke(this, choice);
    }

    // One row per info page, drawn like the other VOID buttons (vacuo-kurallar 15) with a chevron at the end.
    private void BuildInfoRows()
    {
        InfoTitle.Text = _localizer.Get("SettingsInfoTitle");
        foreach (string page in InfoPages)
        {
            var label = new Grid();
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            label.Children.Add(new TextBlock { Text = _localizer.Get($"Info_{page}_Title"), VerticalAlignment = VerticalAlignment.Center });
            var chevron = new FontIcon { Glyph = ChevronGlyph, FontSize = 12 };
            Grid.SetColumn(chevron, 1);
            label.Children.Add(chevron);

            var row = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            row.Click += (_, _) => ShowInfo(page);
            InfoRows.Children.Add(row);
        }
    }

    private void ShowInfo(string page)
    {
        ScreenTitle.Text = _localizer.Get($"Info_{page}_Title");
        ReadingText.Children.Clear();
        foreach (string paragraph in _localizer.Get($"Info_{page}_Body").Split(ParagraphBreak, StringSplitOptions.RemoveEmptyEntries))
        {
            ReadingText.Children.Add(Paragraph(paragraph.Trim()));
        }

        if (page == AboutPage)
        {
            AddAboutDetails();
        }

        SettingsPage.Visibility = Visibility.Collapsed;
        ReadingPage.Visibility = Visibility.Visible;
        ReadingPage.ChangeView(null, 0, null, disableAnimation: true);
    }

    // A paragraph may open with "# heading" on its own line.
    private static StackPanel Paragraph(string paragraph)
    {
        var block = new StackPanel { Spacing = 4 };
        string body = paragraph;
        if (paragraph.StartsWith(HeadingMark, StringComparison.Ordinal))
        {
            int lineEnd = paragraph.IndexOf('\n');
            string heading = lineEnd < 0 ? paragraph : paragraph[..lineEnd];
            body = lineEnd < 0 ? string.Empty : paragraph[(lineEnd + 1)..].Trim();
            block.Children.Add(new TextBlock
            {
                Text = heading[HeadingMark.Length..].Trim(),
                Style = (Style)Application.Current.Resources["CardTitleStyle"],
            });
        }

        if (body.Length > 0)
        {
            block.Children.Add(new TextBlock
            {
                Text = body,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Foreground = (Brush)Application.Current.Resources["InkSecondaryBrush"],
            });
        }

        return block;
    }

    private void AddAboutDetails()
    {
        Version? version = Assembly.GetEntryAssembly()?.GetName().Version;
        var details = new StackPanel { Spacing = 2, Margin = new Thickness(0, 6, 0, 0) };
        foreach (string line in new[]
        {
            _localizer.Format("InfoAboutVersionFormat", version is null ? "?" : version.ToString(3)),
            _localizer.Get("InfoAboutDeveloper"),
            _localizer.Get("InfoAboutCopyright"),
        })
        {
            details.Children.Add(new TextBlock { Text = line, Foreground = (Brush)Application.Current.Resources["InkBrush"] });
        }

        ReadingText.Children.Add(details);
        var gitHub = new Button { Content = _localizer.Get("InfoAboutGitHubButton") };
        ToolTipService.SetToolTip(gitHub, RepositoryAddress.ToString());
        gitHub.Click += async (_, _) => await Launcher.LaunchUriAsync(RepositoryAddress);
        ReadingText.Children.Add(gitHub);
    }

    // Back leaves an info page for the settings first, then the settings for the cards.
    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (ReadingPage.Visibility == Visibility.Visible)
        {
            ShowSettingsPage();
            return;
        }

        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ShowSettingsPage()
    {
        ScreenTitle.Text = _localizer.Get("SettingsTitle");
        ReadingPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
    }
}
