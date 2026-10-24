using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Vacuo.Maintenance;

/// <summary>
/// The few GDI+ calls the notice's black hole needs: anti-aliased, semi-transparent circles and rings, which plain
/// GDI cannot draw. GDI+ ships with Windows, so the helper still needs no UI framework. Colours are 0xAARRGGBB.
/// </summary>
internal static class GdiPlus
{
    public const int SmoothingAntiAlias = 4;
    public const int UnitPixel = 2;

    private const int Ok = 0;

    /// <summary>Starts GDI+ for this process; give the token back to <see cref="Stop"/>.</summary>
    public static nint Start()
    {
        var input = new StartupInput { GdiplusVersion = 1 };
        int status = GdiplusStartup(out nint token, ref input, 0);
        return status == Ok ? token : throw new Win32Exception($"GDI+ did not start (status {status}).");
    }

    public static void Stop(nint token) => GdiplusShutdown(token);

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [DllImport("gdiplus.dll")]
    private static extern int GdiplusStartup(out nint token, ref StartupInput input, nint output);

    [DllImport("gdiplus.dll")]
    private static extern void GdiplusShutdown(nint token);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateFromHDC(nint hdc, out nint graphics);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteGraphics(nint graphics);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetSmoothingMode(nint graphics, int mode);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateSolidFill(uint argb, out nint brush);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetSolidFillColor(nint brush, uint argb);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteBrush(nint brush);

    [DllImport("gdiplus.dll")]
    public static extern int GdipFillEllipse(nint graphics, nint brush, float x, float y, float width, float height);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreatePen1(uint argb, float width, int unit, out nint pen);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeletePen(nint pen);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDrawEllipse(nint graphics, nint pen, float x, float y, float width, float height);
}
