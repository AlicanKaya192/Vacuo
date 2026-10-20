using static Vacuo.Tray.NativeMethods;

namespace Vacuo.Tray;

/// <summary>
/// The tray's only sentences. The tray carries no culture data and cannot read the panel's .resw files,
/// so its few words live here in both languages: --lang decides, else the Windows display language.
/// </summary>
internal sealed record TrayTexts(string PanelFailedTitle, string PanelFailed)
{
    private static readonly TrayTexts Turkish = new(
        "Vacuo",
        "Panel açılamadı: Vacuo.exe bulunamadı ya da başlatılamadı. Vacuo tepside açık kalıyor.");

    private static readonly TrayTexts English = new(
        "Vacuo",
        "The panel could not open: Vacuo.exe was not found or could not start. Vacuo stays in the tray.");

    public static TrayTexts For(IReadOnlyList<string> panelArguments)
    {
        int lang = panelArguments.ToList().IndexOf("--lang");
        bool turkish = lang >= 0 && lang + 1 < panelArguments.Count
            ? panelArguments[lang + 1].StartsWith("tr", StringComparison.OrdinalIgnoreCase)
            : (GetUserDefaultUILanguage() & 0x3FF) == TurkishPrimaryLanguage;
        return turkish ? Turkish : English;
    }
}
