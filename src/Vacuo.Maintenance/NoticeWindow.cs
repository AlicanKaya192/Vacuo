using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vacuo.Core.Notices;
using static Vacuo.Maintenance.NativeMethods;

namespace Vacuo.Maintenance;

/// <summary>
/// Vacuo's own notification (vacuo-kurallar 16, ADR-004): a small VOID card at the bottom right, above the
/// tray, without opening the panel and without taking the focus. It slides up and fades in, stays for
/// <see cref="Visible"/>, then fades out; a click closes it sooner. With Windows animations off it simply
/// appears and disappears (vacuo-kurallar 9). Plain Win32 and GDI, so the helper needs no UI framework.
/// </summary>
internal sealed class NoticeWindow
{
    private const string ClassName = "Vacuo.Notice";
    private const nuint AnimationTimer = 1;
    private const uint FrameMs = 15;
    private const double BaseDpi = 96;

    // Sizes in device-independent pixels.
    private const int WidthDip = 360;
    private const int PaddingDip = 16;
    private const int IconDip = 32;
    private const int IconGapDip = 12;
    private const int TitleGapDip = 4;
    private const int ScreenMarginDip = 12;
    private const int CornerDip = 12;
    private const int SlideDip = 12;
    private const int TitleFontDip = 15;
    private const int BodyFontDip = 13;
    private const string FontFace = "Segoe UI";

    // The VOID palette (App.xaml) as GDI colours, 0x00BBGGRR.
    private const uint SurfaceRaised = 0x002A171D;
    private const uint Line = 0x0041242C;
    private const uint Ink = 0x00F5E7EC;
    private const uint InkSecondary = 0x00BFA1AB;

    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan Visible = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(220);

    // Kept alive for as long as Windows may call it.
    private static readonly WindowProc Procedure = OnMessage;
    private static NoticeWindow? _current;

    private readonly NoticeText _text;
    private readonly bool _animate;
    private readonly Stopwatch _clock = new();
    private nint _window;
    private nint _titleFont;
    private nint _bodyFont;
    private nint _icon;
    private double _scale;
    private int _width;
    private int _height;
    private int _x;
    private int _y;
    private Rect _titleRect;
    private Rect _bodyRect;
    private TimeSpan? _closingAt;

    private NoticeWindow(NoticeText text)
    {
        _text = text;
        _animate = !SystemParametersInfo(SpiGetClientAreaAnimation, 0, out bool animations, 0) || animations;
    }

    /// <summary>Shows the notice and returns when it has gone. <paramref name="sound"/> is a .wav file, or null for silence.</summary>
    public static void Show(NoticeText text, byte[]? sound, string iconPath)
    {
        _ = SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        var notice = new NoticeWindow(text);
        _current = notice;
        GCHandle pinned = sound is null ? default : GCHandle.Alloc(sound, GCHandleType.Pinned);
        try
        {
            notice.Create(iconPath);
            if (sound is not null)
            {
                // Played from memory in the background; the buffer stays pinned until the notice is gone.
                _ = PlaySound(pinned.AddrOfPinnedObject(), 0, SoundMemory | SoundAsync | SoundNoDefault);
            }

            RunMessageLoop();
        }
        finally
        {
            if (sound is not null)
            {
                _ = PlaySound(0, 0, 0);
                pinned.Free();
            }

            notice.Release();
            _current = null;
        }
    }

    private void Create(string iconPath)
    {
        Rect work = default;
        _ = SystemParametersInfo(SpiGetWorkArea, 0, ref work, 0);
        nint monitor = MonitorFromPoint(new Point { X = work.Right - 1, Y = work.Bottom - 1 }, MonitorDefaultToPrimary);
        _scale = GetDpiForMonitor(monitor, EffectiveDpi, out uint dpi, out _) == 0 ? dpi / BaseDpi : 1;

        _titleFont = CreateFont(-Px(TitleFontDip), 0, 0, 0, FontWeightSemiBold, 0, 0, 0, DefaultCharset, 0, 0, ClearTypeQuality, 0, FontFace);
        _bodyFont = CreateFont(-Px(BodyFontDip), 0, 0, 0, FontWeightNormal, 0, 0, 0, DefaultCharset, 0, 0, ClearTypeQuality, 0, FontFace);
        _icon = File.Exists(iconPath) ? LoadImage(0, iconPath, ImageIcon, Px(IconDip), Px(IconDip), LoadFromFile) : 0;
        Layout(work);

        nint instance = GetModuleHandle(null);
        var windowClass = new WindowClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Procedure),
            hInstance = instance,
            lpszClassName = ClassName,
        };
        _ = RegisterClassEx(ref windowClass);

        _window = CreateWindowEx(
            WsExTopmost | WsExToolWindow | WsExNoActivate | WsExLayered, ClassName, "Vacuo", WsPopup,
            _x, _animate ? _y + Px(SlideDip) : _y, _width, _height, 0, 0, instance, 0);
        if (_window == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the notice window.");
        }

        int corner = Px(CornerDip) * 2;
        _ = SetWindowRgn(_window, CreateRoundRectRgn(0, 0, _width + 1, _height + 1, corner, corner), redraw: false);
        _ = SetLayeredWindowAttributes(_window, 0, _animate ? (byte)0 : byte.MaxValue, LayeredAlpha);
        _ = ShowWindow(_window, ShowNoActivate);
        _clock.Start();
        _ = SetTimer(_window, AnimationTimer, FrameMs, 0);
    }

    // The title is one line; the body wraps, and its height sets the card's.
    private void Layout(Rect work)
    {
        int padding = Px(PaddingDip);
        _width = Px(WidthDip);
        int textLeft = padding + (_icon != 0 ? Px(IconDip) + Px(IconGapDip) : 0);
        int textRight = _width - padding;

        nint measuring = CreateCompatibleDC(0);
        try
        {
            _titleRect = Measure(measuring, _titleFont, _text.Title, textLeft, padding, textRight, DrawTextSingleLine);
            _bodyRect = Measure(measuring, _bodyFont, _text.Body, textLeft, _titleRect.Bottom + Px(TitleGapDip), textRight, DrawTextWordBreak);
        }
        finally
        {
            _ = DeleteDC(measuring);
        }

        _height = Math.Max(_bodyRect.Bottom, padding + Px(IconDip)) + padding;
        _x = work.Right - Px(ScreenMarginDip) - _width;
        _y = work.Bottom - Px(ScreenMarginDip) - _height;
    }

    private static Rect Measure(nint hdc, nint font, string text, int left, int top, int right, uint format)
    {
        var rect = new Rect { Left = left, Top = top, Right = right, Bottom = top };
        nint previous = SelectObject(hdc, font);
        _ = DrawText(hdc, text, text.Length, ref rect, format | DrawTextCalcRect | DrawTextNoPrefix);
        _ = SelectObject(hdc, previous);
        rect.Right = right;
        return rect;
    }

    private void Paint()
    {
        nint hdc = BeginPaint(_window, out PaintStruct paint);
        try
        {
            var all = new Rect { Right = _width, Bottom = _height };
            nint surface = CreateSolidBrush(SurfaceRaised);
            _ = FillRect(hdc, ref all, surface);
            _ = DeleteObject(surface);

            nint pen = CreatePen(SolidPen, Math.Max(1, Px(1)), Line);
            nint previousPen = SelectObject(hdc, pen);
            nint previousBrush = SelectObject(hdc, GetStockObject(NullBrush));
            int corner = Px(CornerDip) * 2;
            _ = RoundRect(hdc, 0, 0, _width, _height, corner, corner);
            _ = SelectObject(hdc, previousBrush);
            _ = SelectObject(hdc, previousPen);
            _ = DeleteObject(pen);

            if (_icon != 0)
            {
                _ = DrawIconEx(hdc, Px(PaddingDip), Px(PaddingDip), _icon, Px(IconDip), Px(IconDip), 0, 0, DrawIconNormal);
            }

            _ = SetBkMode(hdc, Transparent);
            DrawLine(hdc, _titleFont, Ink, _text.Title, _titleRect, DrawTextSingleLine | DrawTextEndEllipsis);
            DrawLine(hdc, _bodyFont, InkSecondary, _text.Body, _bodyRect, DrawTextWordBreak);
        }
        finally
        {
            _ = EndPaint(_window, ref paint);
        }
    }

    private static void DrawLine(nint hdc, nint font, uint color, string text, Rect rect, uint format)
    {
        nint previous = SelectObject(hdc, font);
        _ = SetTextColor(hdc, color);
        _ = DrawText(hdc, text, text.Length, ref rect, format | DrawTextNoPrefix);
        _ = SelectObject(hdc, previous);
    }

    private void Tick()
    {
        TimeSpan now = _clock.Elapsed;
        if (_closingAt is null && now >= FadeIn + Visible)
        {
            _closingAt = now;
        }

        if (_closingAt is { } closing)
        {
            double gone = _animate ? (now - closing) / FadeOut : 1;
            if (gone >= 1)
            {
                _ = DestroyWindow(_window);
                return;
            }

            _ = SetLayeredWindowAttributes(_window, 0, (byte)(byte.MaxValue * (1 - gone)), LayeredAlpha);
            return;
        }

        if (_animate && now < FadeIn + TimeSpan.FromMilliseconds(FrameMs))
        {
            double shown = Math.Min(1, now / FadeIn);
            double eased = 1 - Math.Pow(1 - shown, 3);
            _ = SetLayeredWindowAttributes(_window, 0, (byte)(byte.MaxValue * eased), LayeredAlpha);
            _ = SetWindowPos(_window, 0, _x, _y + (int)Math.Round(Px(SlideDip) * (1 - eased)), 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
    }

    private void Close() => _closingAt ??= _clock.Elapsed;

    private void Release()
    {
        foreach (nint font in new[] { _titleFont, _bodyFont }.Where(font => font != 0))
        {
            _ = DeleteObject(font);
        }

        if (_icon != 0)
        {
            _ = DestroyIcon(_icon);
        }
    }

    private int Px(int dip) => (int)Math.Round(dip * _scale);

    private static void RunMessageLoop()
    {
        while (GetMessage(out Message message, 0, 0, 0) > 0)
        {
            _ = TranslateMessage(ref message);
            _ = DispatchMessage(ref message);
        }
    }

    private static nint OnMessage(nint hWnd, uint message, nint wParam, nint lParam)
    {
        NoticeWindow? notice = _current;
        switch (message)
        {
            case WmPaint when notice is not null:
                notice.Paint();
                return 0;
            case WmEraseBackground:
                return 1;
            case WmTimer when notice is not null && (nuint)wParam == AnimationTimer:
                notice.Tick();
                return 0;
            case WmMouseActivate:
                return MouseActivateNoActivate;
            case WmLButtonUp when notice is not null:
                notice.Close();
                return 0;
            case WmDestroy:
                PostQuitMessage(0);
                return 0;
        }

        return DefWindowProc(hWnd, message, wParam, lParam);
    }
}
