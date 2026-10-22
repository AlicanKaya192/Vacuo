using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

namespace Vacuo.App.Visuals;

/// <summary>
/// The graphics card as a sun beside the black hole (Alican, 22 October 2026): the hotter the card, the
/// larger and brighter the sun; while it runs cool, the hole siphons a thick stream of gas off the sun's
/// near side, which falls onto the disk's rim and winds round the hole with it. Colours lean toward the
/// black hole's (white-hot core, violet outer glow), so the two read as one scene.
/// Runs on the Win2D game-loop thread only.
/// </summary>
internal sealed class SunHeat
{
    // The temperature text in PanelWindow.xaml is centred on the same distance from the right edge.
    internal const float RightInset = 100;
    private const float CoreRadius = 13;
    private const int ReachSegments = 40;
    private const int WrapSegments = 170;
    private const float StreamLift = 34;
    private const float EntryShare = 0.96f;
    // Ends just outside the shadow (BlackHoleScene's ShadowShare is 0.2), where the gas is swallowed.
    private const float SwallowShare = 0.21f;
    private const float WrapAngle = 3 * MathF.PI;
    private const float ReachSpeed = 45;
    private const float RimSpeed = 70;
    private const float ClumpsPerSecond = 1.3f;
    private const int Granules = 12;

    private static readonly Color Ember = Color.FromArgb(0xFF, 0xFF, 0x86, 0x5A);
    private static readonly Color Flare = Color.FromArgb(0xFF, 0xFF, 0xE2, 0xB0);
    private static readonly Color WhiteHot = Color.FromArgb(0xFF, 0xFF, 0xF7, 0xEE);
    private static readonly Color Accent = Color.FromArgb(0xFF, 0x9D, 0x7C, 0xFF);
    private static readonly Color Hot = Color.FromArgb(0xFF, 0xF4, 0xEE, 0xFF);
    private static readonly CanvasStrokeStyle RoundEnds = new() { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

    private readonly Vector2[] _path = new Vector2[ReachSegments + WrapSegments + 1];
    // Seconds the gas takes from the sun to each point: clumps of brightness ride on it, so they speed up inward.
    private readonly float[] _travel = new float[ReachSegments + WrapSegments + 1];
    // Whether each segment lies on the disk's near side, drawn in front of the hole.
    private readonly bool[] _inFront = new bool[ReachSegments + WrapSegments + 1];
    private float _time;
    private float? _frameLevel;
    private float _frameRadius;

    /// <summary>0 cool to 1 hot (see Core HeatLevel); null hides the sun. Set from any thread.</summary>
    public float? Level { get; set; }

    public static Vector2 SunCenter(Vector2 size) => new(size.X - RightInset, size.Y * 0.4f);

    public void Advance(TimeSpan elapsed) => _time += (float)Math.Min(elapsed.TotalSeconds, 0.05);

    /// <summary>Before <paramref name="hole"/> is drawn: the sun, and the stream up to the disk's rim and behind the hole.</summary>
    public void DrawBehind(CanvasDrawingSession session, Vector2 size, BlackHoleScene hole)
    {
        // Read once, so both halves of this frame agree even if Level changes in between.
        _frameLevel = Level;
        if (_frameLevel is not { } level)
        {
            return;
        }

        Vector2 sun = SunCenter(size);
        float pulse = 1 + 0.04f * level * MathF.Sin(_time * 3.1f) + 0.015f * level * MathF.Sin(_time * 7.3f);
        _frameRadius = CoreRadius * (0.85f + 0.3f * level) * pulse;
        BuildPath(size, sun, _frameRadius, hole);

        DrawCorona(session, sun, _frameRadius, level);
        DrawStream(session, inFront: false, Siphon(level));
        DrawPhotosphere(session, sun, _frameRadius, level);
    }

    /// <summary>After the black hole: the turns of the stream on the disk's near side, in front of the hole.</summary>
    public void DrawFront(CanvasDrawingSession session)
    {
        if (_frameLevel is { } level)
        {
            DrawStream(session, inFront: true, Siphon(level));
        }
    }

    // A cool card is siphoned hard; a hot one barely, the hole cannot keep up.
    private static float Siphon(float level) => 1 - 0.85f * level;

    /// <summary>
    /// From the sun's near side, over and down onto the disk's rim (arriving along the rim, so the bend is straight
    /// above it), then a narrowing spiral of one and a half turns with the disk's turn, into the shadow's edge.
    /// Gas orbits faster the deeper it falls, so the travel time per pixel shrinks inward.
    /// </summary>
    private void BuildPath(Vector2 size, Vector2 sun, float radius, BlackHoleScene hole)
    {
        Vector2 start = sun + new Vector2(-radius * 0.6f, 0);
        Vector2 entry = hole.DiskPoint(size, 0, EntryShare);
        Vector2 bend = entry + new Vector2(0, -StreamLift * (1 + 0.08f * MathF.Sin(_time * 0.8f)));
        _path[0] = start;
        for (int i = 1; i <= ReachSegments; i++)
        {
            float t = i / (float)ReachSegments;
            _path[i] = (1 - t) * (1 - t) * start + 2 * (1 - t) * t * bend + t * t * entry;
            _travel[i] = _travel[i - 1] + Vector2.Distance(_path[i - 1], _path[i]) / ReachSpeed;
            _inFront[i] = false;
        }

        for (int j = 1; j <= WrapSegments; j++)
        {
            float t = j / (float)WrapSegments;
            float angle = t * WrapAngle;
            float share = EntryShare * MathF.Pow(SwallowShare / EntryShare, MathF.Pow(t, 1.3f));
            int i = ReachSegments + j;
            _path[i] = hole.DiskPoint(size, angle, share);
            _travel[i] = _travel[i - 1] + Vector2.Distance(_path[i - 1], _path[i]) / (RimSpeed / MathF.Sqrt(share));
            _inFront[i] = MathF.Sin(angle - 0.5f * WrapAngle / WrapSegments) >= 0;
        }
    }

    /// <summary>
    /// The stream's segments on one side of the hole: a ribbon tapering from the sun, with clumps of brightness
    /// riding the flow, heating from ember through violet to white as it falls, and fading as the hole swallows it.
    /// </summary>
    private void DrawStream(CanvasDrawingSession session, bool inFront, float siphon)
    {
        for (int i = 1; i < _path.Length; i++)
        {
            if (_inFront[i] != inFront)
            {
                continue;
            }

            float reach = Math.Min(1, i / (float)ReachSegments);
            float fall = Math.Max(0, (i - ReachSegments) / (float)WrapSegments);
            float width = (fall == 0
                ? MathF.Max(2.4f, _frameRadius * 1.5f * MathF.Pow(1 - reach, 2.4f))
                : 2.4f - 1.3f * fall) * (0.55f + 0.45f * siphon);
            float clump = 0.5f + 0.5f * MathF.Sin(MathF.Tau * ClumpsPerSecond * (_travel[i] - _time));
            float swallow = MathF.Min(1, (1 - fall) / 0.12f);
            float alpha = siphon * swallow * (0.45f + 0.55f * clump * clump);
            Color color = fall < 0.5f ? Blend(Ember, Accent, MathF.Max(fall * 2, reach * 0.35f)) : Blend(Accent, Hot, (fall - 0.5f) * 2);

            session.DrawLine(_path[i - 1], _path[i], WithAlpha(color, (0.14f + 0.12f * fall) * alpha), width * 2.6f + 3, RoundEnds);
            session.DrawLine(_path[i - 1], _path[i], WithAlpha(color, 0.8f * alpha), width, RoundEnds);
        }
    }

    // One smooth glow, warm inside and violet at its edge like the black hole's halo.
    private static void DrawCorona(CanvasDrawingSession session, Vector2 sun, float radius, float level)
    {
        float corona = radius * (3.2f + 1.8f * level);
        Color glow = Blend(Ember, Flare, level);
        using var brush = new CanvasRadialGradientBrush(session,
        [
            new CanvasGradientStop { Position = 0, Color = WithAlpha(glow, 0.4f + 0.35f * level) },
            new CanvasGradientStop { Position = 0.3f, Color = WithAlpha(glow, 0.12f + 0.18f * level) },
            new CanvasGradientStop { Position = 0.65f, Color = WithAlpha(Blend(glow, Accent, 0.6f), 0.05f + 0.05f * level) },
            new CanvasGradientStop { Position = 1, Color = WithAlpha(Accent, 0) },
        ])
        {
            Center = sun,
            RadiusX = corona,
            RadiusY = corona,
        };
        session.FillCircle(sun, corona, brush);
    }

    // White-hot middle like the disk's inner edge, darker toward a soft limb, and a thin bright rim echoing the photon ring.
    private void DrawPhotosphere(CanvasDrawingSession session, Vector2 sun, float radius, float level)
    {
        Color edge = Blend(Ember, Flare, 0.3f * level);
        using var brush = new CanvasRadialGradientBrush(session,
        [
            new CanvasGradientStop { Position = 0, Color = WhiteHot },
            new CanvasGradientStop { Position = 0.5f, Color = Blend(Flare, WhiteHot, level) },
            new CanvasGradientStop { Position = 0.88f, Color = edge },
            new CanvasGradientStop { Position = 1, Color = WithAlpha(edge, 0) },
        ])
        {
            Center = sun,
            RadiusX = radius * 1.08f,
            RadiusY = radius * 1.08f,
        };
        session.FillCircle(sun, radius * 1.08f, brush);
        session.DrawCircle(sun, radius, WithAlpha(Flare, 0.35f), 1f);

        // Granulation: small cells brightening and fading across the surface.
        for (int cell = 0; cell < Granules; cell++)
        {
            float angle = cell * 2.4f;
            float distance = radius * 0.7f * MathF.Sqrt((cell + 0.5f) / Granules);
            var point = sun + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            float flicker = 0.5f + 0.5f * MathF.Sin(_time * (1.3f + cell * 0.17f) + cell);
            session.FillCircle(point, radius * 0.12f, WithAlpha(Ember, 0.1f * flicker));
        }
    }

    private static Color WithAlpha(Color color, float alpha) =>
        Color.FromArgb((byte)(255 * Math.Clamp(alpha, 0, 1)), color.R, color.G, color.B);

    private static Color Blend(Color from, Color to, float t) => Color.FromArgb(
        0xFF,
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));
}
