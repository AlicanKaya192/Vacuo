using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace Vacuo.App.Visuals;

/// <summary>A chip in the Disk flow: a category on the left or a bucket on the right, with its colour dot if any.</summary>
internal sealed record FlowChip(string Label, Color? Dot);

/// <summary>One stream of the Disk flow, from a category chip to a bucket chip.</summary>
internal sealed record FlowStream(int From, int To, Color Color, int Particles);

/// <summary>
/// The Disk screen's flow (Aşama 3d A, dilim 2): the categories on the left, the buckets on the
/// right and particles along a curve for every stream, in the colour of the bucket they go to.
/// Laid out after <see cref="FlowMap"/>; built on the UI thread, then advanced and drawn on the
/// Win2D game-loop thread only.
/// </summary>
internal sealed class DiskFlowMap
{
    private const float RowHeight = 32f;
    private const float EdgeMargin = 12f;
    private const float ChipHeight = 24f;
    private const float ChipPadding = 12f;
    private const float DotRadius = 4f;
    private const float DotSpace = 14f;
    private const float MinimumCurveWidth = 80f;
    private const float CyclesPerSecond = 0.22f;

    private static readonly Color Surface = Color.FromArgb(0xFF, 0x1D, 0x17, 0x2A);
    private static readonly Color Line = Color.FromArgb(0xFF, 0x2C, 0x24, 0x41);
    private static readonly Color Ink = Color.FromArgb(0xFF, 0xEC, 0xE7, 0xF5);

    private static readonly CanvasTextFormat LabelFormat = new()
    {
        FontSize = 12,
        VerticalAlignment = CanvasVerticalAlignment.Center,
        WordWrapping = CanvasWordWrapping.NoWrap,
        TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
        TrimmingSign = CanvasTrimmingSign.Ellipsis,
    };

    private readonly Chip[] _sources;
    private readonly Chip[] _sinks;
    private readonly Lane[] _lanes;
    private readonly Particle[] _particles;

    public DiskFlowMap(IReadOnlyList<FlowChip> sources, IReadOnlyList<FlowChip> sinks, IReadOnlyList<FlowStream> streams, Vector2 size)
    {
        // Each column as wide as its longest label; on a narrow screen both give way and trim.
        float sourceWidth = WidestChip(sources);
        float sinkWidth = WidestChip(sinks);
        float room = size.X - MinimumCurveWidth;
        if (sourceWidth + sinkWidth > room)
        {
            float scale = Math.Max(0, room) / (sourceWidth + sinkWidth);
            sourceWidth *= scale;
            sinkWidth *= scale;
        }

        _sources = Column(sources, 0, sourceWidth, size.Y);
        _sinks = Column(sinks, size.X - sinkWidth, sinkWidth, size.Y);
        _lanes = streams.Select(stream => Lane.Between(_sources[stream.From], _sinks[stream.To], stream.Color)).ToArray();

        var particles = new List<Particle>();
        for (int lane = 0; lane < streams.Count; lane++)
        {
            for (int i = 0; i < streams[lane].Particles; i++)
            {
                particles.Add(new Particle
                {
                    Lane = lane,
                    Phase = Random.Shared.NextSingle(),
                    Speed = CyclesPerSecond * (0.75f + Random.Shared.NextSingle() * 0.5f),
                    Drift = (Random.Shared.NextSingle() - 0.5f) * 8f,
                    Size = Random.Shared.NextSingle() < 0.2f ? 2.2f : 1.4f,
                });
            }
        }

        _particles = particles.ToArray();
    }

    /// <summary>The strip's height for this many chips in its taller column.</summary>
    public static double HeightFor(int rows) => rows * RowHeight + 2 * EdgeMargin;

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
            builder.BeginFigure(lane.Start);
            builder.AddCubicBezier(lane.First, lane.Second, lane.End);
            builder.EndFigure(CanvasFigureLoop.Open);
            using CanvasGeometry curve = CanvasGeometry.CreatePath(builder);
            session.DrawGeometry(curve, Color.FromArgb(0x38, lane.Color.R, lane.Color.G, lane.Color.B), 1.5f);
        }

        foreach (Particle particle in _particles)
        {
            Lane lane = _lanes[particle.Lane];
            Vector2 point = FlowMap.Bezier(lane.Start, lane.First, lane.Second, lane.End, particle.Phase);
            // Spread out in the middle of the curve, gathered at both ends.
            float spread = MathF.Sin(particle.Phase * MathF.PI);
            point.Y += particle.Drift * spread;
            byte alpha = (byte)(255 * MathF.Min(1f, spread * 2.2f));
            session.FillCircle(point, particle.Size, Color.FromArgb(alpha, lane.Color.R, lane.Color.G, lane.Color.B));
        }

        foreach (Chip chip in _sources.Concat(_sinks))
        {
            var box = new global::Windows.Foundation.Rect(chip.Left, chip.Top, chip.Width, ChipHeight);
            session.FillRoundedRectangle(box, ChipHeight / 2, ChipHeight / 2, Surface);
            session.DrawRoundedRectangle(box, ChipHeight / 2, ChipHeight / 2, Line, 1.2f);
            float textLeft = chip.Left + ChipPadding;
            if (chip.Content.Dot is { } dot)
            {
                session.FillCircle(new Vector2(textLeft + DotRadius, chip.Top + ChipHeight / 2), DotRadius, dot);
                textLeft += DotSpace;
            }

            var text = new global::Windows.Foundation.Rect(textLeft, chip.Top, Math.Max(0, chip.Left + chip.Width - ChipPadding - textLeft), ChipHeight);
            session.DrawText(chip.Content.Label, text, Ink, LabelFormat);
        }
    }

    private static float WidestChip(IReadOnlyList<FlowChip> chips)
    {
        CanvasDevice device = CanvasDevice.GetSharedDevice();
        float widest = 0;
        foreach (FlowChip chip in chips)
        {
            using var layout = new CanvasTextLayout(device, chip.Label, LabelFormat, float.MaxValue, ChipHeight);
            widest = Math.Max(widest, (float)layout.LayoutBounds.Width + (chip.Dot is null ? 0 : DotSpace));
        }

        return MathF.Ceiling(widest) + 2 * ChipPadding;
    }

    // One column of equal chips, spread evenly between the edge margins.
    private static Chip[] Column(IReadOnlyList<FlowChip> chips, float left, float width, float height)
    {
        float step = (height - 2 * EdgeMargin) / Math.Max(1, chips.Count);
        return chips
            .Select((chip, index) => new Chip(chip, left, EdgeMargin + step * (index + 0.5f) - ChipHeight / 2, width))
            .ToArray();
    }

    private sealed record Chip(FlowChip Content, float Left, float Top, float Width)
    {
        public Vector2 RightEdge => new(Left + Width, Top + ChipHeight / 2);

        public Vector2 LeftEdge => new(Left, Top + ChipHeight / 2);
    }

    private sealed record Lane(Vector2 Start, Vector2 First, Vector2 Second, Vector2 End, Color Color)
    {
        public static Lane Between(Chip source, Chip sink, Color color)
        {
            Vector2 start = source.RightEdge;
            Vector2 end = sink.LeftEdge;
            float reach = (end.X - start.X) * 0.45f;
            return new Lane(start, new Vector2(start.X + reach, start.Y), new Vector2(end.X - reach, end.Y), end, color);
        }
    }

    private struct Particle
    {
        public int Lane;
        public float Phase;
        public float Speed;
        public float Drift;
        public float Size;
    }
}
