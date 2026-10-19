using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace Vacuo.App.Visuals;

/// <summary>
/// The VOID scene: an accretion disk seen at an angle, its particles spiralling
/// into a dark event horizon. Radii are normalised (1 = disk edge) so the scene
/// does not depend on the control size. Runs on the Win2D game-loop thread only.
/// </summary>
internal sealed class VoidField
{
    private const float Tilt = 0.42f;
    private const float Horizon = 0.2f;
    private const float InnerSpawn = 0.3f;
    private const float LightParticleShare = 0.3f;
    private const int MinimumParticles = 200;
    private const double SlowFrameMs = 22;
    private const float TrimFactor = 0.9f;

    private static readonly Color Void = Color.FromArgb(0xFF, 0x0A, 0x08, 0x11);
    private static readonly Color Accent = Color.FromArgb(0xFF, 0x9D, 0x7C, 0xFF);
    private static readonly Color AccentLight = Color.FromArgb(0xFF, 0xD4, 0xC6, 0xFF);

    private readonly Particle[] _particles;
    private int _count;
    private double _averageFrameMs = 16;

    public VoidField(int particleCount)
    {
        _particles = new Particle[particleCount];
        _count = particleCount;
        for (int i = 0; i < _count; i++)
        {
            _particles[i] = Spawn(Random.Shared.NextSingle());
        }
    }

    public int Count => _count;

    public void Advance(TimeSpan elapsed)
    {
        float dt = (float)Math.Min(elapsed.TotalSeconds, 0.05);
        AdaptToFrameTime(elapsed.TotalMilliseconds);

        for (int i = 0; i < _count; i++)
        {
            ref Particle p = ref _particles[i];
            p.Angle += dt * 0.55f / MathF.Sqrt(p.Radius + 0.04f);
            p.Radius -= dt * 0.06f * p.Pull * (1.2f - MathF.Min(1f, p.Radius));
            if (p.Radius < Horizon)
            {
                p = Spawn(InnerSpawn + Random.Shared.NextSingle() * (1 - InnerSpawn));
            }
        }
    }

    public void Draw(CanvasDrawingSession session, Vector2 size)
    {
        var center = new Vector2(size.X / 2, size.Y / 2);
        float diskRadius = MathF.Min(size.X * 0.47f, size.Y * 0.45f / Tilt);

        DrawParticles(session, center, diskRadius, front: false);
        session.FillCircle(center, diskRadius * Horizon, Void);
        session.DrawCircle(center, diskRadius * Horizon, Accent, 1.5f);
        DrawParticles(session, center, diskRadius, front: true);
    }

    // The disk is drawn in two halves so the horizon hides the far side: a cheap 3D effect.
    private void DrawParticles(CanvasDrawingSession session, Vector2 center, float diskRadius, bool front)
    {
        for (int i = 0; i < _count; i++)
        {
            Particle p = _particles[i];
            float sin = MathF.Sin(p.Angle);
            if (sin >= 0 != front)
            {
                continue;
            }

            float x = center.X + MathF.Cos(p.Angle) * p.Radius * diskRadius;
            float y = center.Y + sin * p.Radius * diskRadius * Tilt;
            float nearness = 1 - MathF.Min(1, (p.Radius - Horizon) / (1 - Horizon));
            Color baseColor = p.IsLight ? AccentLight : Accent;
            byte alpha = (byte)(255 * (0.18f + 0.8f * nearness));
            session.FillRectangle(x, y, p.Size, p.Size, Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        }
    }

    // Fewer particles when frames get slow: the effect must never cost the user responsiveness.
    private void AdaptToFrameTime(double frameMs)
    {
        _averageFrameMs = _averageFrameMs * 0.95 + frameMs * 0.05;
        if (_averageFrameMs > SlowFrameMs && _count > MinimumParticles)
        {
            _count = Math.Max(MinimumParticles, (int)(_count * TrimFactor));
            _averageFrameMs = 16;
        }
    }

    private static Particle Spawn(float radius) => new()
    {
        Radius = radius,
        Angle = Random.Shared.NextSingle() * MathF.Tau,
        Pull = 0.6f + Random.Shared.NextSingle() * 0.8f,
        Size = Random.Shared.NextSingle() < 0.12f ? 1.8f : 1.1f,
        IsLight = Random.Shared.NextSingle() < LightParticleShare,
    };

    private struct Particle
    {
        public float Radius;
        public float Angle;
        public float Pull;
        public float Size;
        public bool IsLight;
    }
}
