using static Vacuo.Maintenance.GdiPlus;

namespace Vacuo.Maintenance;

/// <summary>
/// The panel's black hole (Visuals/BlackHoleScene) made small for the notice (Alican, 23 October 2026: "bildirimlerde
/// ki karadeliği en son yaptıklarımız gibi yapsana hatta animasyonlu"): a soft halo, the tilted gas disk that is hotter
/// inside and brighter on the side turning toward the viewer, the far side bent into an arc over the shadow, the
/// photon ring, and dust falling in. Fewer points than the panel's, for a 70-pixel scene drawn with GDI+.
/// </summary>
internal sealed class NoticeHole
{
    private const float Tilt = 0.22f;
    private const float ShadowShare = 0.22f;
    private const float DiskInnerShare = 0.3f;
    private const int DiskRows = 6;
    private const int DiskPointsPerRow = 90;
    private const int LensPoints = 80;
    private const int LensLayers = 3;
    private const int HaloRings = 8;
    private const float DiskTurnsPerSecond = 0.18f;
    private const int DustCount = 90;
    // The disk radius the dot sizes below were chosen for; at a larger scale the dots grow with it.
    private const float DesignRadius = 34f;

    // The VOID palette (App.xaml) as 0xRRGGBB.
    private const uint Void = 0x0A0811;
    private const uint Accent = 0x9D7CFF;
    private const uint AccentLight = 0xD4C6FF;
    private const uint Hot = 0xF4EEFF;
    private const uint Deep = 0x5B3FC4;

    private readonly Dust[] _dust = new Dust[DustCount];
    private float _diskAngle;

    public NoticeHole()
    {
        for (int i = 0; i < DustCount; i++)
        {
            _dust[i] = SpawnDust(DiskInnerShare + Random.Shared.NextSingle() * (1.1f - DiskInnerShare));
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        float dt = (float)Math.Min(elapsed.TotalSeconds, 0.05);
        _diskAngle = (_diskAngle + DiskTurnsPerSecond * MathF.Tau * dt) % (MathF.Tau * 1000);
        for (int i = 0; i < DustCount; i++)
        {
            ref Dust grain = ref _dust[i];
            grain.Angle += dt * 0.5f / MathF.Sqrt(grain.Radius + 0.04f);
            grain.Radius -= dt * 0.05f * grain.Pull;
            if (grain.Radius < ShadowShare)
            {
                grain = SpawnDust(0.9f + Random.Shared.NextSingle() * 0.2f);
            }
        }
    }

    /// <summary>Draws the scene centred on (<paramref name="x"/>, <paramref name="y"/>) with the disk <paramref name="radius"/> pixels wide each way.</summary>
    public void Draw(nint hdc, float x, float y, float radius)
    {
        if (GdipCreateFromHDC(hdc, out nint graphics) != 0)
        {
            return;
        }

        _ = GdipSetSmoothingMode(graphics, SmoothingAntiAlias);
        _ = GdipCreateSolidFill(0, out nint brush);
        try
        {
            var canvas = new Canvas(graphics, brush, x, y, radius / DesignRadius);
            float shadow = radius * ShadowShare;
            for (int ring = HaloRings; ring >= 1; ring--)
            {
                canvas.Circle(0, 0, shadow * (1 + 1.6f * ring / HaloRings), Argb(Accent, 0x07));
            }

            DrawDisk(canvas, radius, farSide: true);
            DrawDust(canvas, radius, farSide: true);
            DrawLensedDisk(canvas, shadow);
            canvas.Circle(0, 0, shadow, Argb(Void, 0xFF));
            DrawPhotonRing(graphics, x, y, shadow, canvas.Unit);
            DrawDisk(canvas, radius, farSide: false);
            DrawDust(canvas, radius, farSide: false);
        }
        finally
        {
            _ = GdipDeleteBrush(brush);
            _ = GdipDeleteGraphics(graphics);
        }
    }

    private void DrawDisk(Canvas canvas, float radius, bool farSide)
    {
        for (int row = 0; row < DiskRows; row++)
        {
            float t = row / (float)(DiskRows - 1);
            float ringRadius = radius * (DiskInnerShare + (1 - DiskInnerShare) * t);
            float turn = _diskAngle * MathF.Pow(DiskInnerShare / (DiskInnerShare + (1 - DiskInnerShare) * t), 1.5f);
            float fade = 0.15f + 0.85f * (1 - t) * (1 - t);
            uint color = Blend(t < 0.5f ? Hot : AccentLight, t < 0.5f ? AccentLight : Deep, t < 0.5f ? t * 2 : (t - 0.5f) * 2);
            float dot = (1.3f - 0.4f * t) * canvas.Unit;
            for (int i = 0; i < DiskPointsPerRow; i++)
            {
                float angle = i * MathF.Tau / DiskPointsPerRow + row * 0.37f;
                // Screen y grows downward: the upper half is the far side.
                if (MathF.Sin(angle) < 0 != farSide)
                {
                    continue;
                }

                // The left side turns toward the viewer and shines brighter, as in the panel.
                float doppler = 0.3f + 0.7f * (1 - MathF.Cos(angle)) / 2;
                float clumps = 0.65f + 0.35f * MathF.Sin(6 * (angle - turn) + row * 1.7f);
                canvas.Circle(
                    MathF.Cos(angle) * ringRadius,
                    MathF.Sin(angle) * ringRadius * Tilt,
                    dot,
                    Argb(color, (byte)(255 * doppler * clumps * fade)));
            }
        }
    }

    private static void DrawLensedDisk(Canvas canvas, float shadow)
    {
        for (int i = 0; i < LensPoints; i++)
        {
            float angle = i * MathF.Tau / LensPoints;
            float sin = MathF.Sin(angle);
            bool above = sin < 0;
            float strength = above ? MathF.Pow(-sin, 1.2f) : 0.3f * sin * sin;
            float doppler = 0.45f + 0.55f * (1 - MathF.Cos(angle)) / 2;
            for (int layer = 0; layer < LensLayers; layer++)
            {
                float ringRadius = shadow * (1.08f + layer * (above ? 0.1f : 0.06f));
                byte alpha = (byte)(235 * strength * doppler * (1 - layer * 0.28f));
                canvas.Circle(MathF.Cos(angle) * ringRadius, sin * ringRadius, canvas.Unit, Argb(layer == 0 ? Hot : AccentLight, alpha));
            }
        }
    }

    // A thin bright ring with a fading glow on both sides, not a hard outline.
    private static void DrawPhotonRing(nint graphics, float x, float y, float shadow, float unit)
    {
        float r = shadow * 1.02f;
        foreach ((uint color, float width) in new[] { (Argb(Accent, 0x26), 4f), (Argb(AccentLight, 0x48), 2f), (Argb(Hot, 0x9A), 1f) })
        {
            if (GdipCreatePen1(color, width * unit, UnitPixel, out nint pen) == 0)
            {
                _ = GdipDrawEllipse(graphics, pen, x - r, y - r, 2 * r, 2 * r);
                _ = GdipDeletePen(pen);
            }
        }
    }

    private void DrawDust(Canvas canvas, float radius, bool farSide)
    {
        foreach (Dust grain in _dust)
        {
            float sin = MathF.Sin(grain.Angle);
            if (sin < 0 != farSide)
            {
                continue;
            }

            float nearness = 1 - MathF.Min(1, (grain.Radius - ShadowShare) / (1.1f - ShadowShare));
            canvas.Circle(
                MathF.Cos(grain.Angle) * grain.Radius * radius,
                sin * grain.Radius * radius * Tilt,
                grain.Size * canvas.Unit,
                Argb(AccentLight, (byte)(255 * (0.15f + 0.5f * nearness))));
        }
    }

    private static Dust SpawnDust(float radius) => new()
    {
        Radius = radius,
        Angle = Random.Shared.NextSingle() * MathF.Tau,
        Pull = 0.6f + Random.Shared.NextSingle() * 0.8f,
        Size = Random.Shared.NextSingle() < 0.15f ? 0.9f : 0.6f,
    };

    private static uint Argb(uint rgb, byte alpha) => (uint)alpha << 24 | rgb;

    private static uint Blend(uint from, uint to, float t)
    {
        uint Channel(int shift)
        {
            float a = (from >> shift) & 0xFF;
            float b = (to >> shift) & 0xFF;
            return (uint)(a + (b - a) * t) << shift;
        }

        return Channel(16) | Channel(8) | Channel(0);
    }

    // One brush, recoloured per dot: cheaper than a brush per dot.
    private readonly record struct Canvas(nint Graphics, nint Brush, float X, float Y, float Unit)
    {
        public void Circle(float dx, float dy, float radius, uint argb)
        {
            _ = GdipSetSolidFillColor(Brush, argb);
            _ = GdipFillEllipse(Graphics, Brush, X + dx - radius, Y + dy - radius, 2 * radius, 2 * radius);
        }
    }

    private struct Dust
    {
        public float Radius;
        public float Angle;
        public float Pull;
        public float Size;
    }
}
