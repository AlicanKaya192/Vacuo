using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace Vacuo.App.Visuals;

/// <summary>
/// The panel's black hole, seen almost edge-on (Alican, 22 October 2026: the dots alone were "tam bir karadelik
/// gibi değil"): a soft halo, a glowing gas disk that is hotter inside and brighter on the side turning toward
/// the viewer, the disk's far side bent by gravity into an arc over the shadow and a faint one under it, the
/// shadow with a soft photon ring, and a few dust grains still falling in. Sizes are shares of the disk radius,
/// so the scene does not depend on the control size. Runs on the Win2D game-loop thread only.
/// </summary>
internal sealed class BlackHoleScene
{
    internal const float Tilt = 0.22f;
    private const float ShadowShare = 0.2f;
    private const float DiskInnerShare = 0.28f;
    private const int DiskRows = 9;
    private const int DiskPointsPerRow = 160;
    private const int LensPoints = 140;
    private const int LensLayers = 4;
    private const int HaloRings = 10;
    private const float DiskTurnsPerSecond = 0.18f;
    private const int DustCount = 600;
    private const int MinimumDust = 150;
    private const double SlowFrameMs = 22;

    private static readonly Color Void = Color.FromArgb(0xFF, 0x0A, 0x08, 0x11);
    private static readonly Color Accent = Color.FromArgb(0xFF, 0x9D, 0x7C, 0xFF);
    private static readonly Color AccentLight = Color.FromArgb(0xFF, 0xD4, 0xC6, 0xFF);
    private static readonly Color Hot = Color.FromArgb(0xFF, 0xF4, 0xEE, 0xFF);
    private static readonly Color Deep = Color.FromArgb(0xFF, 0x5B, 0x3F, 0xC4);

    private readonly Dust[] _dust = new Dust[DustCount];
    private readonly float _centerShare;
    private readonly float _widthShare;
    private int _dustCount = DustCount;
    private double _averageFrameMs = 16;
    private float _diskAngle;

    /// <param name="centerShare">Where the hole sits across the control: the panel keeps it left of centre for the sun.</param>
    /// <param name="widthShare">The disk's radius as a share of the control's width, at most.</param>
    public BlackHoleScene(float centerShare = 0.5f, float widthShare = 0.45f)
    {
        _centerShare = centerShare;
        _widthShare = widthShare;
        for (int i = 0; i < DustCount; i++)
        {
            _dust[i] = SpawnDust(DiskInnerShare + Random.Shared.NextSingle() * (1.15f - DiskInnerShare));
        }
    }

    public int Count => _dustCount;

    /// <summary>How hard the hole pulls; 1 is the calm scene, more while something is falling in. Set from any thread.</summary>
    public float PullStrength { get; set; } = 1f;

    public Vector2 Center(Vector2 size) => new(size.X * _centerShare, size.Y / 2);

    // Also bounded by the height, so the bent arc over the shadow never touches the control's top edge.
    public float DiskRadius(Vector2 size) => MathF.Min(MathF.Min(size.X * _widthShare, size.Y * 0.44f / Tilt), size.Y * 1.7f);

    /// <summary>
    /// A point in the disk's plane, <paramref name="share"/> of the disk radius out. Angle 0 is the right edge; growing
    /// angles run over the near side, the way the disk's clumps move.
    /// </summary>
    public Vector2 DiskPoint(Vector2 size, float angle, float share) =>
        Center(size) + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * Tilt) * DiskRadius(size) * share;

    public void Advance(TimeSpan elapsed)
    {
        float dt = (float)Math.Min(elapsed.TotalSeconds, 0.05);
        AdaptToFrameTime(elapsed.TotalMilliseconds);
        float pull = PullStrength;
        _diskAngle = (_diskAngle + DiskTurnsPerSecond * (0.7f + 0.3f * pull) * MathF.Tau * dt) % (MathF.Tau * 1000);
        for (int i = 0; i < _dustCount; i++)
        {
            ref Dust grain = ref _dust[i];
            grain.Angle += dt * 0.5f * (0.7f + 0.3f * pull) / MathF.Sqrt(grain.Radius + 0.04f);
            grain.Radius -= dt * 0.035f * grain.Pull * pull;
            if (grain.Radius < ShadowShare)
            {
                grain = SpawnDust(0.9f + Random.Shared.NextSingle() * 0.25f);
            }
        }
    }

    public void Draw(CanvasDrawingSession session, Vector2 size)
    {
        Vector2 center = Center(size);
        float radius = DiskRadius(size);
        float shadow = radius * ShadowShare;

        for (int ring = HaloRings; ring >= 1; ring--)
        {
            session.FillCircle(center, shadow * (1 + 1.6f * ring / HaloRings), WithAlpha(Accent, 0x06));
        }

        DrawDisk(session, center, radius, farSide: true);
        DrawDust(session, center, radius, farSide: true);
        DrawLensedDisk(session, center, shadow);
        session.FillCircle(center, shadow, Void);
        DrawPhotonRing(session, center, shadow);
        DrawDisk(session, center, radius, farSide: false);
        DrawDust(session, center, radius, farSide: false);
    }

    /// <summary>Rings of glowing gas, brightest and whitest inside; inner rings turn faster, so bright clumps shear.</summary>
    private void DrawDisk(CanvasDrawingSession session, Vector2 center, float radius, bool farSide)
    {
        for (int row = 0; row < DiskRows; row++)
        {
            float t = row / (float)(DiskRows - 1);
            float ringRadius = radius * (DiskInnerShare + (1 - DiskInnerShare) * t);
            float turn = _diskAngle * MathF.Pow(DiskInnerShare / (DiskInnerShare + (1 - DiskInnerShare) * t), 1.5f);
            float fade = 0.1f + 0.9f * (1 - t) * (1 - t);
            Color color = Blend(t < 0.5f ? Hot : AccentLight, t < 0.5f ? AccentLight : Deep, t < 0.5f ? t * 2 : (t - 0.5f) * 2);
            float dot = 1.7f - 0.7f * t;
            for (int i = 0; i < DiskPointsPerRow; i++)
            {
                float angle = i * MathF.Tau / DiskPointsPerRow + row * 0.37f;
                // Screen y grows downward: the upper half is the far side.
                if (MathF.Sin(angle) < 0 != farSide)
                {
                    continue;
                }

                // The left side turns toward the viewer and shines brighter (relativistic beaming).
                float doppler = 0.3f + 0.7f * (1 - MathF.Cos(angle)) / 2;
                float clumps = 0.65f + 0.35f * MathF.Sin(6 * (angle - turn) + row * 1.7f);
                var point = new Vector2(center.X + MathF.Cos(angle) * ringRadius, center.Y + MathF.Sin(angle) * ringRadius * Tilt);
                session.FillCircle(point, dot, WithAlpha(color, (byte)(255 * doppler * clumps * fade)));
            }
        }
    }

    /// <summary>The far side of the disk, bent over the top of the shadow into a bright arc and under it into a faint one.</summary>
    private static void DrawLensedDisk(CanvasDrawingSession session, Vector2 center, float shadow)
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
                float ringRadius = shadow * (1.08f + layer * (above ? 0.09f : 0.05f));
                var point = new Vector2(center.X + MathF.Cos(angle) * ringRadius, center.Y + sin * ringRadius);
                byte alpha = (byte)(235 * strength * doppler * (1 - layer * 0.22f));
                session.FillCircle(point, 1.4f, WithAlpha(layer == 0 ? Hot : AccentLight, alpha));
            }
        }
    }

    // Soft, not a hard outline: a thin bright ring with a fading glow on both sides.
    private static void DrawPhotonRing(CanvasDrawingSession session, Vector2 center, float shadow)
    {
        session.DrawCircle(center, shadow * 1.02f, WithAlpha(Accent, 0x22), 6f);
        session.DrawCircle(center, shadow * 1.02f, WithAlpha(AccentLight, 0x40), 3f);
        session.DrawCircle(center, shadow * 1.02f, WithAlpha(Hot, 0x90), 1f);
    }

    private void DrawDust(CanvasDrawingSession session, Vector2 center, float radius, bool farSide)
    {
        for (int i = 0; i < _dustCount; i++)
        {
            Dust grain = _dust[i];
            float sin = MathF.Sin(grain.Angle);
            if (sin < 0 != farSide)
            {
                continue;
            }

            var point = new Vector2(center.X + MathF.Cos(grain.Angle) * grain.Radius * radius, center.Y + sin * grain.Radius * radius * Tilt);
            float nearness = 1 - MathF.Min(1, (grain.Radius - ShadowShare) / (1.15f - ShadowShare));
            session.FillRectangle(point.X, point.Y, grain.Size, grain.Size, WithAlpha(AccentLight, (byte)(255 * (0.1f + 0.45f * nearness))));
        }
    }

    // Fewer grains when frames get slow: the effect must never cost the user responsiveness.
    private void AdaptToFrameTime(double frameMs)
    {
        _averageFrameMs = _averageFrameMs * 0.95 + frameMs * 0.05;
        if (_averageFrameMs > SlowFrameMs && _dustCount > MinimumDust)
        {
            _dustCount = Math.Max(MinimumDust, (int)(_dustCount * 0.9f));
            _averageFrameMs = 16;
        }
    }

    private static Dust SpawnDust(float radius) => new()
    {
        Radius = radius,
        Angle = Random.Shared.NextSingle() * MathF.Tau,
        Pull = 0.6f + Random.Shared.NextSingle() * 0.8f,
        Size = Random.Shared.NextSingle() < 0.12f ? 1.6f : 1f,
    };

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Blend(Color from, Color to, float t) => Color.FromArgb(
        0xFF,
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));

    private struct Dust
    {
        public float Radius;
        public float Angle;
        public float Pull;
        public float Size;
    }
}
