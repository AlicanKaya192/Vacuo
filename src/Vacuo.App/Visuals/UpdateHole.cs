using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace Vacuo.App.Visuals;

/// <summary>
/// The update screen's picture (vacuo-kurulum-guncelleme-plani K3): the VOID black hole, pulling harder
/// while the new version comes in, with a ring of dots around its disk that lights up as the download
/// fills. Progress is set on the UI thread; advanced and drawn on the Win2D game-loop thread only.
/// </summary>
internal sealed class UpdateHole
{
    private const int ParticleCount = 900;
    private const float DownloadPull = 3f;
    private const float RingScale = 1.06f;
    private const int RingDots = 120;

    private static readonly Color Accent = Color.FromArgb(0xFF, 0x9D, 0x7C, 0xFF);
    private static readonly Color AccentLight = Color.FromArgb(0xFF, 0xD4, 0xC6, 0xFF);
    private static readonly Color Unlit = Color.FromArgb(0x40, 0x9D, 0x7C, 0xFF);

    private readonly VoidField _field = new(ParticleCount);
    private volatile float _progress;

    public void Show(float progress, bool downloading)
    {
        _progress = Math.Clamp(progress, 0f, 1f);
        _field.PullStrength = downloading ? DownloadPull : 1f;
    }

    public void Advance(TimeSpan elapsed) => _field.Advance(elapsed);

    public void Draw(CanvasDrawingSession session, Vector2 size)
    {
        _field.Draw(session, size);
        Vector2 center = size / 2;
        float radius = VoidField.DiskRadiusFor(size) * RingScale;
        int lit = (int)MathF.Round(_progress * RingDots);
        for (int i = 0; i < RingDots; i++)
        {
            // From the top, clockwise, like a clock hand.
            float angle = -MathF.PI / 2 + i * MathF.Tau / RingDots;
            var point = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * VoidField.Tilt);
            bool isLit = i < lit;
            session.FillCircle(point, isLit ? 1.7f : 1f, isLit ? (i == lit - 1 ? AccentLight : Accent) : Unlit);
        }
    }
}
