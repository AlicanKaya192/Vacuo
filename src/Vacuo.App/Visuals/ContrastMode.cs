using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Vacuo.App.Visuals;

/// <summary>
/// Whether a Windows contrast theme is on (vacuo-gelistirme-plani Aşama 3f). The XAML follows it through its
/// HighContrast theme dictionary; the Win2D scenes are drawn in fixed VOID colours, so the decorative ones step
/// aside and the disk ring takes the theme's own colours. Every number a scene shows is also on screen as text.
/// The state comes from SystemParametersInfo: AccessibilitySettings' change event needs a CoreWindow, which a
/// desktop app does not have, and subscribing to it crashed the panel at start.
/// </summary>
internal static class ContrastMode
{
    private const uint GetHighContrast = 0x0042;
    private const uint HighContrastOn = 0x1;

    private static readonly UISettings Colours = new();
    private static volatile bool _isOn = ReadIsOn();

    static ContrastMode()
    {
        // Windows raises this when a contrast theme is turned on or off; if it cannot, Refresh on every opening still catches it.
        try
        {
            Colours.ColorValuesChanged += (_, _) => Refresh();
        }
        catch (COMException)
        {
        }
    }

    /// <summary>Raised, off the UI thread when Windows reports it, when the user turns a contrast theme on or off.</summary>
    public static event EventHandler? Changed;

    /// <summary>Cached, so the game-loop threads can ask every frame.</summary>
    public static bool IsOn => _isOn;

    public static Color Text => Colours.UIElementColor(UIElementType.WindowText);

    public static Color Highlight => Colours.UIElementColor(UIElementType.Highlight);

    /// <summary>Reads the setting again; raises <see cref="Changed"/> if it differs.</summary>
    public static void Refresh()
    {
        bool isOn = ReadIsOn();
        if (isOn != _isOn)
        {
            _isOn = isOn;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    private static bool ReadIsOn()
    {
        var info = new HighContrastInfo { Size = Marshal.SizeOf<HighContrastInfo>() };
        return SystemParametersInfo(GetHighContrast, (uint)info.Size, ref info, 0) && (info.Flags & HighContrastOn) != 0;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref HighContrastInfo info, uint update);

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrastInfo
    {
        public int Size;
        public uint Flags;
        public nint DefaultScheme;
    }
}
