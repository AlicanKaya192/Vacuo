using System.Text;

namespace Vacuo.Core.Hosting;

/// <summary>
/// The text the panel asks the tray to show over its icon: while an update downloads behind a hidden panel,
/// its percent and time left (Alican, 20 October 2026). Sent with WM_COPYDATA tagged
/// <see cref="PanelProtocol.TooltipDataTag"/> as UTF-16. Windows shows at most <see cref="MaxChars"/>
/// characters on one line, so longer text is cut with an ellipsis. Empty means the tray's own "Vacuo".
/// </summary>
public static class TrayTooltip
{
    public const int MaxChars = 127;

    private const char FirstPrintable = ' ';
    private const string Ellipsis = "…";

    public static byte[] Encode(string text)
    {
        string line = new(text.Select(character => character < FirstPrintable ? ' ' : character).ToArray());
        if (line.Length > MaxChars)
        {
            line = line[..(MaxChars - Ellipsis.Length)] + Ellipsis;
        }

        return Encoding.Unicode.GetBytes(line);
    }

    /// <summary>Null for anything <see cref="Encode"/> would not have sent.</summary>
    public static string? Decode(byte[] data)
    {
        if (data.Length % 2 != 0 || data.Length / 2 > MaxChars)
        {
            return null;
        }

        string text = Encoding.Unicode.GetString(data);
        return text.Any(character => character < FirstPrintable) ? null : text;
    }
}
