namespace Vacuo.Core.Hosting;

/// <summary>
/// Which notifications from the tray icon open or close the panel (vacuo-gelistirme-plani Aşama 3f, keyboard).
/// The icon uses NOTIFYICON_VERSION_4, so the event is the low word of lParam and the icon's id the high word.
/// A click (left or right button up) toggles, as before; so does the keyboard's select (Win+B, the arrows to
/// the icon, then Enter or Space). NIN_SELECT, which follows every left click, is left out so a click does not
/// toggle twice, and a keyboard select close behind another is taken as the same key press.
/// </summary>
public sealed class TrayIconInput
{
    public const uint LeftButtonUp = 0x0202;
    public const uint RightButtonUp = 0x0205;
    /// <summary>NIN_KEYSELECT.</summary>
    public const uint KeyboardSelect = 0x0401;

    public static readonly TimeSpan RepeatWindow = TimeSpan.FromMilliseconds(400);

    private TimeSpan? _lastKeyboardSelect;

    /// <param name="lParam">As the icon's callback message carries it.</param>
    /// <param name="now">Any steady clock; only the time between two notifications counts.</param>
    public bool Toggles(nint lParam, TimeSpan now)
    {
        uint notification = (uint)(lParam & 0xFFFF);
        if (notification is LeftButtonUp or RightButtonUp)
        {
            return true;
        }

        if (notification != KeyboardSelect)
        {
            return false;
        }

        bool sameKeyPress = _lastKeyboardSelect is { } last && now - last < RepeatWindow;
        _lastKeyboardSelect = now;
        return !sameKeyPress;
    }
}
