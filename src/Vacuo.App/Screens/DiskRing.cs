using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
using Vacuo.App.Visuals;
using Vacuo.Core.Disk;
using Vacuo.Core.Scanning;
using Windows.UI;

namespace Vacuo.App.Screens;

/// <summary>
/// The Disk card's ring: the whole drive as a circle, each bucket as an arc in its palette A
/// colour, the rest (other files and free space) in the line colour.
/// </summary>
internal static class DiskRing
{
    private const float StrokeWidth = 10f;
    private const float GapRadians = 0.035f;
    private const float StartAngle = -MathF.PI / 2;

    private static readonly Color RestColor = ColorHelper.FromArgb(0xFF, 0x2C, 0x24, 0x41);

    // In a contrast theme every bucket takes the theme's highlight, so the legend squares and row edges follow it too (Aşama 3f).
    public static Color ColorOf(Bucket bucket) => ContrastMode.IsOn ? ContrastMode.Highlight : bucket switch
    {
        Bucket.RebuildsItself => ColorHelper.FromArgb(0xFF, 0x9D, 0x7C, 0xFF),
        Bucket.Yours => ColorHelper.FromArgb(0xFF, 0xD4, 0xC6, 0xFF),
        Bucket.SameFiles => ColorHelper.FromArgb(0xFF, 0x66, 0x50, 0xB4),
        Bucket.Keep => ColorHelper.FromArgb(0xFF, 0x4A, 0x3F, 0x66),
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null),
    };

    public static void Draw(CanvasDrawingSession session, Vector2 size, DiskOverview? overview)
    {
        var center = size / 2;
        float radius = MathF.Min(size.X, size.Y) / 2 - StrokeWidth;
        // In a contrast theme (Aşama 3f): a thin ring in the theme's text colour; the buckets take its highlight (ColorOf).
        bool contrast = ContrastMode.IsOn;
        session.DrawCircle(center, radius, contrast ? ContrastMode.Text : RestColor, contrast ? 2f : StrokeWidth);
        if (overview is null)
        {
            return;
        }

        float start = StartAngle;
        foreach (DiskSegment segment in overview.Segments)
        {
            float sweep = (float)(segment.Fraction * 2 * Math.PI);
            if (sweep > GapRadians)
            {
                DrawArc(session, center, radius, start, sweep - GapRadians, ColorOf(segment.Bucket));
            }

            start += sweep;
        }
    }

    private static void DrawArc(CanvasDrawingSession session, Vector2 center, float radius, float start, float sweep, Color color)
    {
        using var builder = new CanvasPathBuilder(session);
        builder.BeginFigure(center + radius * new Vector2(MathF.Cos(start), MathF.Sin(start)));
        builder.AddArc(center, radius, radius, start, sweep);
        builder.EndFigure(CanvasFigureLoop.Open);
        using CanvasGeometry geometry = CanvasGeometry.CreatePath(builder);
        session.DrawGeometry(geometry, color, StrokeWidth);
    }
}
