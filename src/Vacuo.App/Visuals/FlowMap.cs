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

    // The folder as a black hole seen almost edge-on (Alican, 20 October 2026: first "the inside is empty",
    // then "not like a real black hole"): a dark shadow with a thin photon ring, the far side of the disk bent
    // over the top of the shadow by its gravity, and the near side crossing in front of it.
    private const float ShadowRadius = 9f;
    private const float DiskInnerRadius = 12f;
    private const float DiskOuterRadius = 32f;
    private const float DiskTilt = 0.17f;
    private const int DiskRows = 6;
    private const int DiskPointsPerRow = 110;
    private const int LensPoints = 90;
    private const int LensLayers = 3;
    private const float DiskTurnsPerSecond = 0.22f;
    private const int HaloRings = 8;

    private static readonly Color Surface = Color.FromArgb(0xFF, 0x1D, 0x17, 0x2A);
    private static readonly Color Void = Color.FromArgb(0xFF, 0x0A, 0x08, 0x11);
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
    private float _diskAngle;

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
        _diskAngle = (_diskAngle + DiskTurnsPerSecond * MathF.Tau * seconds) % MathF.Tau;
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

        DrawSource(session);
        session.DrawText(_sourceLabel, new Vector2(_source.X, _source.Y + SourceRadius + 12), Ink, LabelFormat);

        foreach (Lane lane in _lanes)
        {
            var box = new global::Windows.Foundation.Rect(lane.Chip.X, lane.Chip.Y, lane.Width, ChipHeight);
            session.FillRoundedRectangle(box, ChipHeight / 2, ChipHeight / 2, Surface);
            session.DrawRoundedRectangle(box, ChipHeight / 2, ChipHeight / 2, lane.Target.IsNew ? Accent : Line, 1.2f);
            session.DrawText(lane.Target.Label, new Vector2(lane.Chip.X + lane.Width / 2, lane.Chip.Y + ChipHeight / 2), Ink, LabelFormat);
        }
    }

    /// <summary>
    /// The folder as a black hole: a soft glow, the far half of the disk, its image bent around the shadow, the
    /// shadow with its photon ring, then the near half of the disk crossing in front.
    /// </summary>
    private void DrawSource(CanvasDrawingSession session)
    {
        for (int ring = 1; ring <= HaloRings; ring++)
        {
            session.FillCircle(_source, ShadowRadius + (SourceRadius - ShadowRadius) * ring / HaloRings, WithAlpha(Accent, 0x07));
        }

        DrawDisk(session, farSide: true);
        DrawLensedDisk(session);
        session.FillCircle(_source, ShadowRadius, Void);
        session.DrawCircle(_source, ShadowRadius + 0.6f, WithAlpha(AccentLight, 0xD0), 1f);
        DrawDisk(session, farSide: false);
    }

    /// <summary>
    /// The disk as rings of glowing gas, brightest inside. The right side turns toward the viewer and shines
    /// brighter; inner rings turn faster, so the bright clumps shear as they go round.
    /// </summary>
    private void DrawDisk(CanvasDrawingSession session, bool farSide)
    {
        for (int row = 0; row < DiskRows; row++)
        {
            float t = row / (float)(DiskRows - 1);
            float radius = DiskInnerRadius + (DiskOuterRadius - DiskInnerRadius) * t;
            float turn = _diskAngle * MathF.Pow(DiskInnerRadius / radius, 1.5f);
            float fade = 0.15f + 0.85f * (1 - t) * (1 - t);
            Color color = t < 0.4f ? AccentLight : Accent;
            for (int i = 0; i < DiskPointsPerRow; i++)
            {
                float angle = i * MathF.Tau / DiskPointsPerRow;
                // Screen y grows downward: the upper half is the far side.
                if (MathF.Sin(angle) < 0 != farSide)
                {
                    continue;
                }

                float doppler = 0.35f + 0.65f * (1 + MathF.Cos(angle)) / 2;
                float clumps = 0.7f + 0.3f * MathF.Sin(5 * (angle - turn) + row * 1.3f);
                var point = new Vector2(_source.X + MathF.Cos(angle) * radius, _source.Y + MathF.Sin(angle) * radius * DiskTilt);
                session.FillCircle(point, 1.1f, WithAlpha(color, (byte)(255 * doppler * clumps * fade)));
            }
        }
    }

    /// <summary>The far side of the disk, bent by gravity into a bright arc over the shadow and a faint one under it.</summary>
    private void DrawLensedDisk(CanvasDrawingSession session)
    {
        for (int i = 0; i < LensPoints; i++)
        {
            float angle = i * MathF.Tau / LensPoints;
            float sin = MathF.Sin(angle);
            bool above = sin < 0;
            float strength = above ? MathF.Pow(-sin, 1.5f) : 0.35f * sin * sin;
            float doppler = 0.5f + 0.5f * (1 + MathF.Cos(angle)) / 2;
            for (int layer = 0; layer < LensLayers; layer++)
            {
                float radius = ShadowRadius + 1.4f + layer * (above ? 1.6f : 0.8f);
                var point = new Vector2(_source.X + MathF.Cos(angle) * radius, _source.Y + sin * radius);
                byte alpha = (byte)(230 * strength * doppler * (1 - layer * 0.3f));
                session.FillCircle(point, 1f, WithAlpha(layer == 0 ? AccentLight : Accent, alpha));
            }
        }
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

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
