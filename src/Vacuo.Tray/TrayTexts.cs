using Vacuo.Core.Settings;
using static Vacuo.Tray.NativeMethods;

namespace Vacuo.Tray;

/// <summary>
/// The tray's only sentences. The tray carries no culture data and cannot read the panel's .resw files,
/// so its few words live here in both languages: --lang decides, else the choice in Settings, where
/// Automatic means Turkish on a Turkish Windows and English on any other (LanguageRule).
/// </summary>
internal sealed record TrayTexts(string PanelFailedTitle, string PanelFailed)
{
    private static readonly TrayTexts Turkish = new(
        "Vacuo",
        "Panel açılamadı: Vacuo.exe bulunamadı ya da başlatılamadı. Vacuo tepside açık kalıyor.");

    private static readonly TrayTexts English = new(
        "Vacuo",
        "The panel could not open: Vacuo.exe was not found or could not start. Vacuo stays in the tray.");

    public static TrayTexts For(IReadOnlyList<string> panelArguments, LanguageChoice choice)
    {
        int lang = panelArguments.ToList().IndexOf("--lang");
        string language = lang >= 0 && lang + 1 < panelArguments.Count
            ? panelArguments[lang + 1]
            : LanguageRule.Resolve(choice, WindowsLanguage());
        return language == LanguageRule.Turkish || language.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? Turkish : English;
    }

    // Without culture data the Windows display language comes as a number; only Turkish matters here.
    private static string WindowsLanguage() =>
        (GetUserDefaultUILanguage() & 0x3FF) == TurkishPrimaryLanguage ? LanguageRule.Turkish : string.Empty;
}
