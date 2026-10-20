using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vacuo.App.Localization;
using Vacuo.Core.Settings;

namespace Vacuo.App.Screens;

/// <summary>
/// The Settings screen (Aşama 3c): for now the language. Each change is saved at once; the panel reads
/// its language when it starts, so a new one shows from the next opening.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    private static readonly LanguageChoice[] Languages = Enum.GetValues<LanguageChoice>();

    private Localizer _localizer = null!;
    private SettingsStore _store = null!;

    public SettingsView()
    {
        InitializeComponent();
    }

    internal event EventHandler? BackRequested;

    internal void Initialize(Localizer localizer, SettingsStore store)
    {
        _localizer = localizer;
        _store = store;
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
