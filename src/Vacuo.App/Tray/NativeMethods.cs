using System.Runtime.InteropServices;

namespace Vacuo.App.Tray;

internal static class NativeMethods
{
    public const uint NimAdd = 0x0;
    public const uint NimDelete = 0x2;
    public const uint NimSetVersion = 0x4;

    public const uint NifMessage = 0x1;
    public const uint NifIcon = 0x2;
    public const uint NifTip = 0x4;
    public const uint NifShowTip = 0x80;

    public const uint NotifyIconVersion4 = 4;

    public const uint WmApp = 0x8000;
    public const uint WmContextMenu = 0x007B;
    public const uint NinSelect = 0x0400;
    public const uint NinKeySelect = 0x0401;

    public const uint ImageIcon = 1;
    public const uint LoadFromFile = 0x10;
    public const int SmallIconWidthMetric = 49;
    public const int SmallIconHeightMetric = 50;

    public const uint WmCopyData = 0x004A;
    public const uint WmNcLeftButtonDown = 0x00A1;
    public const nint HtCaption = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern nint SendMessage(nint hWnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct CopyDataStruct
    {
        public nint dwData;
        public int cbData;
        public nint lpData;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint SubclassProc(nint hWnd, uint message, nint wParam, nint lParam, nuint subclassId, nuint refData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NotifyIconData
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowSubclass(nint hWnd, SubclassProc proc, nuint subclassId, nuint refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc proc, nuint subclassId);

    [DllImport("comctl32.dll")]
    public static extern nint DefSubclassProc(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);

    public const int DwmWindowAttributeCloak = 13;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hWnd, int attribute, ref int value, int size);
}
