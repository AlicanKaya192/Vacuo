using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vacuo.App.Localization;
using Vacuo.Core.Settings;

namespace Vacuo.App.Screens;

/// <summary>
/// The Settings screen (Aşama 3c): the language and starting with Windows. Each change is saved at
/// once; the panel reads its language when it starts, so a new one shows from the next opening.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    private static readonly LanguageChoice[] Languages = Enum.GetValues<LanguageChoice>();

    private Localizer _localizer = null!;
    private SettingsStore _store = null!;
    private StartupRegistration? _startup;

    public SettingsView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    /// <param name="startup">Null when the tray's program is unknown (the panel runs without the tray): the card is hidden.</param>
    internal void Initialize(Localizer localizer, SettingsStore store, StartupRegistration? startup)
    {
        _localizer = localizer;
        _store = store;
        _startup = startup;
        StartupCard.Visibility = startup is null ? Visibility.Collapsed : Visibility.Visible;
        StartupTitle.Text = localizer.Get("SettingsStartup");
        StartupHint.Text = localizer.Get("SettingsStartupHint");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(StartupBox, StartupTitle.Text);
        BackButton.Content = localizer.Get("DiskBackButton");
        ScreenTitle.Text = localizer.Get("SettingsTitle");
        LanguageTitle.Text = localizer.Get("SettingsLanguage");
        LanguageHint.Text = localizer.Get("SettingsLanguageHint");
        LanguageNote.Text = localizer.Get("SettingsLanguageNextOpen");
        LanguagePicker.SetItems(Languages.Select(choice => (localizer.Get($"Language_{choice}"), (object)choice)));
        LanguagePicker.SelectionChanged += OnLanguageChanged;
    }

    /// <summary>Shows what is saved now; the file may have changed since the screen was made.</summary>
    internal void Refresh()
    {
        LanguagePicker.SelectedIndex = Array.IndexOf(Languages, _store.Load().Language);
        LanguageNote.Visibility = Visibility.Collapsed;
        StartupNote.Visibility = Visibility.Collapsed;
        if (_startup is not null)
        {
            StartupBox.IsChecked = _startup.IsOn;
        }
    }

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

        _store.Save(_store.Load() with { Language = choice });
        LanguageNote.Visibility = Visibility.Visible;
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
