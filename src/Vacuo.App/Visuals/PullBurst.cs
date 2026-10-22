using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.Foundation;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace Vacuo.App.Visuals;

/// <summary>One moved item: where its row was and the short name it carries into the void.</summary>
internal sealed record PulledItem(Vector2 Start, string Label);

/// <summary>
/// What a move looks like: the screen dims, the black hole (the panel's <see cref="BlackHoleScene"/>)
/// opens in the middle and the moved items, one after another, leave their rows, circle the hole in
/// the plane of its disk and fall in, each dragging a ribbon of light that heats up as it falls. Built on the UI thread, then advanced and drawn on the Win2D game-loop thread only.
/// </summary>
internal sealed class PullBurst
{
    private const float FadeInSeconds = 0.35f;
    private const float TravelSeconds = 1.7f;
    private const float FadeOutSeconds = 0.4f;
    private const float MaximumStaggerSeconds = 0.35f;
    private const float AllStaggersSeconds = 1.4f;
    private const float Turns = 1.25f;
    private const float HoleBoxSize = 260f;
    private const float HolePull = 2.5f;
    private const int TrailSegments = 18;
    // How far behind its chip a ribbon reaches, in progress along the fall.
    private const float TrailLength = 0.22f;
    private const float BackdropOpacity = 0.78f;
    private const float ChipHeight = 26f;
    private const float ChipCharacterWidth = 7.2f;
    private const float ChipPadding = 20f;

    private static readonly Color Void = Color.FromArgb(0xFF, 0x0A, 0x08, 0x11);
    private static readonly Color Surface = Color.FromArgb(0xFF, 0x1D, 0x17, 0x2A);
    private static readonly Color Accent = Color.FromArgb(0xFF, 0x9D, 0x7C, 0xFF);
    private static readonly Color Ink = Color.FromArgb(0xFF, 0xEC, 0xE7, 0xF5);
    private static readonly Color Hot = Color.FromArgb(0xFF, 0xF4, 0xEE, 0xFF);
    private static readonly CanvasStrokeStyle RoundEnds = new() { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

    private static readonly CanvasTextFormat LabelFormat = new()
    {
        FontSize = 13,
        HorizontalAlignment = CanvasHorizontalAlignment.Center,
        VerticalAlignment = CanvasVerticalAlignment.Center,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };

    private readonly BlackHoleScene _hole = new() { PullStrength = HolePull };
    private readonly Item[] _items;
    private readonly Vector2 _center;
    private readonly Vector2 _size;
    private readonly float _stagger;
    private float _elapsed;

    public PullBurst(IReadOnlyList<PulledItem> items, Vector2 size)
    {
        _size = size;
        _center = size / 2;
        _stagger = items.Count > 1 ? MathF.Min(MaximumStaggerSeconds, AllStaggersSeconds / (items.Count - 1)) : 0;
        _items = items.Select(item =>
        {
            Vector2 offset = item.Start - _center;
            return new Item(item.Label, offset.Length(), MathF.Atan2(offset.Y, offset.X));
        }).ToArray();
    }

    public TimeSpan Duration => TimeSpan.FromSeconds(
        FadeInSeconds + _stagger * Math.Max(0, _items.Length - 1) + TravelSeconds + FadeOutSeconds);

    public void Advance(TimeSpan elapsed)
    {
        _elapsed += (float)Math.Min(elapsed.TotalSeconds, 0.05);
        _hole.Advance(elapsed);
    }

    public void Draw(CanvasDrawingSession session)
    {
        float total = (float)Duration.TotalSeconds;
        float presence = Math.Clamp(MathF.Min(_elapsed / FadeInSeconds, (total - _elapsed) / FadeOutSeconds), 0f, 1f);

        session.FillRectangle(0, 0, _size.X, _size.Y, WithAlpha(Void, BackdropOpacity * presence));
        using (session.CreateLayer(presence))
        {
            session.Transform = Matrix3x2.CreateTranslation(_center - new Vector2(HoleBoxSize / 2));
            _hole.Draw(session, new Vector2(HoleBoxSize));
            session.Transform = Matrix3x2.Identity;
        }

        DrawTrail(session);
        for (int i = 0; i < _items.Length; i++)
        {
            DrawChip(session, _items[i], Progress(i, lag: 0));
        }
    }

    // The ribbon thickens toward the chip and fades toward its tail; it whitens as the item nears the hole.
    private void DrawTrail(CanvasDrawingSession session)
    {
        for (int i = 0; i < _items.Length; i++)
        {
            float head = Progress(i, lag: 0);
            if (head <= 0f || head >= 1f)
            {
                continue;
            }

            float vanish = MathF.Min(1f, (1f - head) / 0.25f);
            Vector2 previous = PositionOf(_items[i], MathF.Max(0f, head - TrailLength));
            for (int step = 1; step <= TrailSegments; step++)
            {
                float along = step / (float)TrailSegments;
                float progress = MathF.Max(0f, head - TrailLength * (1 - along));
                Vector2 point = PositionOf(_items[i], progress);
                float fall = Fall(progress);
                float width = (1f + 3f * along) * (1f - 0.6f * fall);
                Color color = Blend(Accent, Hot, fall);
                float alpha = along * vanish;
                session.DrawLine(previous, point, WithAlpha(color, 0.18f * alpha), width * 2.6f + 3f, RoundEnds);
                session.DrawLine(previous, point, WithAlpha(color, 0.8f * alpha), width, RoundEnds);
                previous = point;
            }
        }
    }

    private void DrawChip(CanvasDrawingSession session, Item item, float progress)
    {
        if (progress >= 1f)
        {
            return;
        }

        float fall = Fall(progress);
        float scale = 1f - 0.85f * fall;
        float opacity = progress < 0.75f ? 1f : (1f - progress) / 0.25f;
        Vector2 position = PositionOf(item, progress);
        float width = item.Label.Length * ChipCharacterWidth + ChipPadding;
        var bounds = new Rect(-width / 2, -ChipHeight / 2, width, ChipHeight);

        session.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(position);
        session.FillRoundedRectangle(bounds, 6, 6, WithAlpha(Surface, opacity));
        session.DrawRoundedRectangle(bounds, 6, 6, WithAlpha(Accent, opacity), 1f);
        session.DrawText(item.Label, bounds, WithAlpha(Ink, opacity), LabelFormat);
        session.Transform = Matrix3x2.Identity;
    }

    // 0 before the item leaves its row, 1 once it has fallen in.
    private float Progress(int itemIndex, float lag) =>
        Math.Clamp((_elapsed - FadeInSeconds - itemIndex * _stagger - lag) / TravelSeconds, 0f, 1f);

    // Slow at first, then falling in: the pull grows as the item nears the hole.
    private static float Fall(float progress) => MathF.Pow(progress, 1.5f);

    private Vector2 PositionOf(Item item, float progress)
    {
        float fall = Fall(progress);
        float radius = item.Radius * (1 - fall);
        float angle = item.Angle + fall * Turns * MathF.Tau;
        // The hole's disk is seen at an angle; items settle into that plane as they fall.
        float tilt = 1 - (1 - BlackHoleScene.Tilt) * MathF.Min(1, fall * 2);
        return _center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * tilt);
    }

    private static Color WithAlpha(Color color, float opacity) =>
        Color.FromArgb((byte)(255 * Math.Clamp(opacity, 0f, 1f)), color.R, color.G, color.B);

    private static Color Blend(Color from, Color to, float t) => Color.FromArgb(
        0xFF,
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));

    private sealed record Item(string Label, float Radius, float Angle);
}
