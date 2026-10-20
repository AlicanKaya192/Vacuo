using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace Vacuo.App.Visuals;

/// <summary>One subfolder in the Düzen preview: its name, how many files go there, and whether Vacuo makes it.</summary>
internal sealed record FlowTarget(string Label, int Count, bool IsNew);

/// <summary>
/// The Düzen preview as motion (B-161, dilim 4): the folder on the left, its subfolders on the right and
/// a stream of particles along a curve to each, as many as the files going there. Built on the UI
/// thread, then advanced and drawn on the Win2D game-loop thread only.
/// </summary>
internal sealed class FlowMap
{
    /// <summary>Folders drawn one by one; the view folds the rest into one "+N more" chip.</summary>
    public const int MaximumTargetsShown = 6;

    /// <summary>The strip's height for this many chips, so they never overlap or touch its edges.</summary>
    public static double HeightFor(int chips) => Math.Clamp(chips * RowHeight + 2 * EdgeMargin, 120, 250);

    private const float RowHeight = 32f;
    private const float EdgeMargin = 12f;
    private const float SourceX = 70f;
    private const float TargetMargin = 16f;
    private const float ChipHeight = 24f;
    private const float ChipCharacterWidth = 7.2f;
    private const float ChipPadding = 22f;
    private const float SourceRadius = 22f;
    private const int ParticlesPerFile = 10;
    private const int MinimumParticlesPerTarget = 8;
    private const int MaximumParticles = 700;
    private const float CyclesPerSecond = 0.32f;

    private static readonly Color Surface = Color.FromArgb(0xFF, 0x1D, 0x17, 0x2A);
    private static readonly Color Line = Color.FromArgb(0xFF, 0x2C, 0x24, 0x41);
    private static readonly Color Accent = Color.FromArgb(0xFF, 0x9D, 0x7C, 0xFF);
    private static readonly Color AccentLight = Color.FromArgb(0xFF, 0xD4, 0xC6, 0xFF);
    private static readonly Color Ink = Color.FromArgb(0xFF, 0xEC, 0xE7, 0xF5);
    private static readonly Color CurveColor = Color.FromArgb(0x38, 0x9D, 0x7C, 0xFF);

    private static readonly CanvasTextFormat LabelFormat = new()
    {
        FontSize = 12,
        HorizontalAlignment = CanvasHorizontalAlignment.Center,
        VerticalAlignment = CanvasVerticalAlignment.Center,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };

    private readonly string _sourceLabel;
    private readonly Vector2 _source;
    private readonly Lane[] _lanes;
    private readonly Particle[] _particles;

    public FlowMap(string sourceLabel, IReadOnlyList<FlowTarget> targets, Vector2 size)
    {
        _sourceLabel = sourceLabel;
        _source = new Vector2(SourceX, size.Y / 2);
        // One column of equal chips, spread evenly between the edge margins.
        float width = targets.Count == 0 ? 0 : targets.Max(target => target.Label.Length) * ChipCharacterWidth + ChipPadding;
        float step = (size.Y - 2 * EdgeMargin) / Math.Max(1, targets.Count);
        _lanes = targets.Select((target, index) =>
        {
            var chip = new Vector2(size.X - TargetMargin - width, EdgeMargin + step * (index + 0.5f) - ChipHeight / 2);
            return new Lane(target, chip, width, new Vector2(chip.X, chip.Y + ChipHeight / 2));
        }).ToArray();

        int total = _lanes.Sum(lane => Math.Max(MinimumParticlesPerTarget, lane.Target.Count * ParticlesPerFile));
        float scale = total > MaximumParticles ? (float)MaximumParticles / total : 1f;
        var particles = new List<Particle>();
        for (int lane = 0; lane < _lanes.Length; lane++)
        {
            int count = Math.Max(1, (int)(Math.Max(MinimumParticlesPerTarget, _lanes[lane].Target.Count * ParticlesPerFile) * scale));
            for (int i = 0; i < count; i++)
            {
                particles.Add(new Particle
                {
                    Lane = lane,
                    Phase = Random.Shared.NextSingle(),
                    Speed = CyclesPerSecond * (0.75f + Random.Shared.NextSingle() * 0.5f),
                    Drift = (Random.Shared.NextSingle() - 0.5f) * 10f,
                    Size = Random.Shared.NextSingle() < 0.2f ? 2.2f : 1.4f,
                    IsLight = Random.Shared.NextSingle() < 0.3f,
                });
            }
        }

        _particles = particles.ToArray();
    }

    public void Advance(TimeSpan elapsed)
    {
        float seconds = (float)elapsed.TotalSeconds;
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle particle = ref _particles[i];
            particle.Phase += particle.Speed * seconds;
            if (particle.Phase >= 1f)
            {
                particle.Phase -= 1f;
            }
        }
    }

    public void Draw(CanvasDrawingSession session)
    {
        foreach (Lane lane in _lanes)
        {
            using var builder = new CanvasPathBuilder(session);
            builder.BeginFigure(_source);
            (Vector2 first, Vector2 second) = Controls(lane);
            builder.AddCubicBezier(first, second, lane.End);
            builder.EndFigure(CanvasFigureLoop.Open);
            using CanvasGeometry curve = CanvasGeometry.CreatePath(builder);
            session.DrawGeometry(curve, CurveColor, 1.5f);
        }

        foreach (Particle particle in _particles)
        {
            Lane lane = _lanes[particle.Lane];
            (Vector2 first, Vector2 second) = Controls(lane);
            Vector2 point = Bezier(_source, first, second, lane.End, particle.Phase);
            // Spread out in the middle of the curve, gathered at both ends.
            float spread = MathF.Sin(particle.Phase * MathF.PI);
            point.Y += particle.Drift * spread;
            byte alpha = (byte)(255 * MathF.Min(1f, spread * 2.2f));
            Color color = particle.IsLight ? AccentLight : Accent;
            session.FillCircle(point, particle.Size, Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        session.FillCircle(_source, SourceRadius, Surface);
        session.DrawCircle(_source, SourceRadius, Accent, 1.5f);
        session.DrawText(_sourceLabel, new Vector2(_source.X, _source.Y + SourceRadius + 12), Ink, LabelFormat);

        foreach (Lane lane in _lanes)
        {
            var box = new global::Windows.Foundation.Rect(lane.Chip.X, lane.Chip.Y, lane.Width, ChipHeight);
            session.FillRoundedRectangle(box, ChipHeight / 2, ChipHeight / 2, Surface);
            session.DrawRoundedRectangle(box, ChipHeight / 2, ChipHeight / 2, lane.Target.IsNew ? Accent : Line, 1.2f);
            session.DrawText(lane.Target.Label, new Vector2(lane.Chip.X + lane.Width / 2, lane.Chip.Y + ChipHeight / 2), Ink, LabelFormat);
        }
    }

    private (Vector2 First, Vector2 Second) Controls(Lane lane)
    {
        float reach = (lane.End.X - _source.X) * 0.45f;
        return (new Vector2(_source.X + reach, _source.Y), new Vector2(lane.End.X - reach, lane.End.Y));
    }

    internal static Vector2 Bezier(Vector2 start, Vector2 first, Vector2 second, Vector2 end, float t)
    {
        float u = 1 - t;
        return u * u * u * start + 3 * u * u * t * first + 3 * u * t * t * second + t * t * t * end;
    }

    private sealed record Lane(FlowTarget Target, Vector2 Chip, float Width, Vector2 End);

    private struct Particle
    {
        public int Lane;
        public float Phase;
        public float Speed;
        public float Drift;
        public float Size;
        public bool IsLight;
    }
}
