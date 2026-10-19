using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace Vacuo.App.Visuals;

/// <summary>One moved item: where its row was and the short name it carries into the void.</summary>
internal sealed record PulledItem(Vector2 Start, string Label);

/// <summary>
/// What a move looks like: the screen dims, the VOID black hole opens in the middle and the moved
/// items, one after another, leave their rows, circle the hole in the plane of its disk and fall
/// in. Built on the UI thread, then advanced and drawn on the Win2D game-loop thread only.
/// </summary>
internal sealed class PullBurst
{
    private const float FadeInSeconds = 0.35f;
    private const float TravelSeconds = 1.7f;
    private const float FadeOutSeconds = 0.4f;
    private const float MaximumStaggerSeconds = 0.35f;
    private const float AllStaggersSeconds = 1.4f;
    private const float Turns = 1.25f;
    // The hole's disk is seen at an angle; items settle into that plane as they fall.
    private const float DiskTilt = 0.42f;
    private const float HoleBoxSize = 260f;
    private const int HoleParticles = 700;
    private const int TrailParticlesPerItem = 50;
    private const float MaximumTrailLagSeconds = 0.3f;
    private const float BackdropOpacity = 0.78f;
    private const float ChipHeight = 26f;
    private const float ChipCharacterWidth = 7.2f;
    private const float ChipPadding = 20f;

    private static readonly Color Void = Color.FromArgb(0xFF, 0x0A, 0x08, 0x11);
    private static readonly Color Surface = Color.FromArgb(0xFF, 0x1D, 0x17, 0x2A);
    private static readonly Color Accent = Color.FromArgb(0xFF, 0x9D, 0x7C, 0xFF);
    private static readonly Color AccentLight = Color.FromArgb(0xFF, 0xD4, 0xC6, 0xFF);
    private static readonly Color Ink = Color.FromArgb(0xFF, 0xEC, 0xE7, 0xF5);

    private static readonly CanvasTextFormat LabelFormat = new()
    {
        FontSize = 13,
        HorizontalAlignment = CanvasHorizontalAlignment.Center,
        VerticalAlignment = CanvasVerticalAlignment.Center,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };

    private readonly VoidField _hole = new(HoleParticles);
    private readonly Item[] _items;
    private readonly Trail[] _trail;
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

        _trail = new Trail[_items.Length * TrailParticlesPerItem];
        for (int i = 0; i < _trail.Length; i++)
        {
            _trail[i] = new Trail
            {
                ItemIndex = i / TrailParticlesPerItem,
                Lag = Random.Shared.NextSingle() * MaximumTrailLagSeconds,
                Scatter = new Vector2(Spread(14), Spread(6)),
                Size = Random.Shared.NextSingle() < 0.15f ? 2f : 1.2f,
                IsLight = Random.Shared.NextSingle() < 0.35f,
            };
        }
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

    private void DrawTrail(CanvasDrawingSession session)
    {
        foreach (Trail particle in _trail)
        {
            float progress = Progress(particle.ItemIndex, particle.Lag);
            if (progress <= 0f || progress >= 1f)
            {
                continue;
            }

            Vector2 position = PositionOf(_items[particle.ItemIndex], progress) + particle.Scatter * (1 - progress);
            Color color = particle.IsLight ? AccentLight : Accent;
            session.FillRectangle(position.X, position.Y, particle.Size, particle.Size, WithAlpha(color, 0.9f * (1 - progress)));
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
        float tilt = 1 - (1 - DiskTilt) * MathF.Min(1, fall * 2);
        return _center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * tilt);
    }

    private static Color WithAlpha(Color color, float opacity) =>
        Color.FromArgb((byte)(255 * Math.Clamp(opacity, 0f, 1f)), color.R, color.G, color.B);

    private static float Spread(float half) => (Random.Shared.NextSingle() * 2 - 1) * half;

    private sealed record Item(string Label, float Radius, float Angle);

    private struct Trail
    {
        public int ItemIndex;
        public float Lag;
        public Vector2 Scatter;
        public float Size;
        public bool IsLight;
    }
}
