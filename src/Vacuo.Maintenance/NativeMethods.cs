using System.Runtime.InteropServices;

namespace Vacuo.Maintenance;

internal static class NativeMethods
{
    public const uint WmDestroy = 0x0002;
    public const uint WmPaint = 0x000F;
    public const uint WmEraseBackground = 0x0014;
    public const uint WmMouseActivate = 0x0021;
    public const uint WmTimer = 0x0113;
    public const uint WmLButtonUp = 0x0202;
    public const int MouseActivateNoActivate = 3;
    public const int AllowAnyProcess = -1;

    public const uint WsPopup = 0x80000000;
    public const uint WsExTopmost = 0x00000008;
    public const uint WsExToolWindow = 0x00000080;
    public const uint WsExLayered = 0x00080000;
    public const uint WsExNoActivate = 0x08000000;
    public const int ShowNoActivate = 4;
    public const uint LayeredAlpha = 0x2;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;

    public const uint SpiGetWorkArea = 0x0030;
    public const uint SpiGetClientAreaAnimation = 0x1042;
    public const uint MonitorDefaultToPrimary = 1;
    public const int EffectiveDpi = 0;
    public const nint PerMonitorAwareV2 = -4;

    public const int FontWeightNormal = 400;
    public const int FontWeightSemiBold = 600;
    public const uint DefaultCharset = 1;
    public const uint ClearTypeQuality = 5;
    public const int Transparent = 1;
    public const uint DrawTextWordBreak = 0x0010;
    public const uint DrawTextSingleLine = 0x0020;
    public const uint DrawTextCalcRect = 0x0400;
    public const uint DrawTextNoPrefix = 0x0800;
    public const uint DrawTextEndEllipsis = 0x8000;
    public const int SolidPen = 0;
    public const int NullBrush = 5;
    public const uint SourceCopy = 0x00CC0020;

    public const uint SoundAsync = 0x0001;
    public const uint SoundNoDefault = 0x0002;
    public const uint SoundMemory = 0x0004;

    public delegate nint WindowProc(nint hWnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Cursor;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaintStruct
    {
        public nint Hdc;
        public int Erase;
        public Rect Paint;
        public int Restore;
        public int IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WindowClassEx
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProc(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(nint hWnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(nint hWnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    public static extern nuint SetTimer(nint hWnd, nuint id, uint elapseMs, nint timerProc);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out Message message, nint hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    public static extern nint BeginPaint(nint hWnd, out PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EndPaint(nint hWnd, ref PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(uint action, uint param, ref Rect value, uint winIni);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessDpiAwarenessContext(nint context);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DrawText(nint hdc, string text, int length, ref Rect rect, uint format);

    [DllImport("user32.dll")]
    public static extern int FillRect(nint hdc, ref Rect rect, nint brush);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InvalidateRect(nint hWnd, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [DllImport("gdi32.dll")]
    public static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern nint CreateFont(
        int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut,
        uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [DllImport("gdi32.dll")]
    public static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    public static extern nint CreatePen(int style, int width, uint color);

    [DllImport("gdi32.dll")]
    public static extern nint GetStockObject(int index);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hdc, nint gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RoundRect(nint hdc, int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    public static extern int SetBkMode(nint hdc, int mode);

    [DllImport("gdi32.dll")]
    public static extern uint SetTextColor(nint hdc, uint color);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? moduleName);

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PlaySound(nint sound, nint module, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int processId);
}
