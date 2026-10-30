using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Vacuo.Core.Disk;
using Windows.UI;

namespace Vacuo.App.Visuals;

/// <summary>
/// The cleanup as a black hole tearing its planets apart (Aşama 3j, Alican, 27 October 2026: the planets stay where
/// they are and break up under its pull, rocks flying off toward it, "az fizik düşün"). Seven planets with noise-made
/// surfaces sit round the hole. The side of each that faces it crumbles along a ragged, glowing front that eats into
/// the planet as the cleanup goes on; rocks and embers break off the front and fall in under gravity, swirling as
/// they go. At 100% no planet is left. <see cref="CleanupPace"/> decides how far the scene shows. Reports come from
/// the UI thread, drawing runs on the Win2D game-loop thread; one lock keeps them apart.
/// </summary>
internal sealed class DevourScene
{
    private const float OrbitShareX = 0.4f;
    private const float OrbitShareY = 0.34f;
    private const float SpinPerSecond = 0.012f;

    // The shortest the pace lets a whole run take; the debris is thickest when the shown share moves this fast.
    private const float FastestRunSeconds = 2.8f;

    private const int MaximumPieces = 1500;
    private const float PiecesPerSecondIdle = 8;
    private const float PiecesPerSecondFull = 170;
    private const float RockShare = 0.42f;

    // Gravity is set so a piece let go at the planets' orbit falls in about this fast; drag turns orbits into spirals.
    private const float FallSeconds = 1.3f;
    private const float Drag = 0.45f;
    private const float FinalFlareSeconds = 0.7f;
    private const float LongestDrainSeconds = 2f;
    private const int GlowRings = 24;
    private const int RockShapes = 10;
    private const int TextureWidth = 256;
    private const int TextureHeight = 128;

    private static readonly Color AccentLight = Color.FromArgb(0xFF, 0xD4, 0xC6, 0xFF);
    private static readonly Color Hot = Color.FromArgb(0xFF, 0xFF, 0xF2, 0xD8);
    private static readonly Color Fire = Color.FromArgb(0xFF, 0xFF, 0x86, 0x30);
    private static readonly Color Ember = Color.FromArgb(0xFF, 0xFF, 0x5A, 0x1E);

    // Where the breaking front stands for each 1/128 of a planet gone (see FrontFor).
    private static readonly float[] FrontTable = Enumerable.Range(0, 129).Select(i =>
    {
        // The share of a unit disc with x below f is (π − acos f + f·√(1 − f²)) / π; solved for f by halving.
        double left = 1 - i / 128.0;
        double low = -1;
        double high = 1;
        for (int step = 0; step < 40; step++)
        {
            double f = (low + high) / 2;
            double share = (Math.PI - Math.Acos(f) + f * Math.Sqrt(1 - f * f)) / Math.PI;
            (low, high) = share < left ? (f, high) : (low, f);
        }

        return (float)((low + high) / 2);
    }).ToArray();

    private readonly object _gate = new();
    private readonly BlackHoleScene _hole = new(widthShare: 0.17f);
    private readonly Planet[] _planets;
    private readonly float[] _owed;
    private readonly List<Piece> _pieces = [];
    private readonly CleanupPace _pace;
    private readonly Random _random = new(11);
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private float _clock;
    private float _activity;
    private float _doneAt = -1;

    // Taken from the last frame, so pieces can be spawned and moved between frames.
    private Vector2 _center = new(270, 150);
    private Vector2 _orbitSize = new(216, 102);
    private float _shadow = 18;
    private float _diskRadius = 92;

    private CanvasDevice? _device;
    private CanvasRadialGradientBrush? _shade;
    private CanvasRadialGradientBrush? _limb;
    private CanvasGeometry[] _rocks = [];

    public DevourScene()
    {
        var random = new Random(27);
        PlanetKind[] kinds = Enum.GetValues<PlanetKind>();
        _planets = kinds.Select((kind, index) => Planet.Make(kind, index * MathF.Tau / kinds.Length + 0.3f, random)).ToArray();
        _owed = new float[_planets.Length];
        _pace = new CleanupPace(_planets.Length);
    }

    /// <summary>The share shown, 0 to 1; the view writes it under the hole.</summary>
    public double Shown
    {
        get
        {
            lock (_gate)
            {
                return _pace.Shown;
            }
        }
    }

    /// <summary>Completes once every planet is gone, the last pieces have fallen in and the flare has faded.</summary>
    public Task Done => _done.Task;

    public void Report(double fraction)
    {
        lock (_gate)
        {
            _pace.Report(fraction);
        }
    }

    public void ExpectNoReports()
    {
        lock (_gate)
        {
            _pace.ExpectNoReports();
        }
    }

    public void Finish()
    {
        lock (_gate)
        {
            _pace.Finish();
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        float seconds = (float)Math.Min(elapsed.TotalSeconds, 0.05);
        if (seconds <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _clock += seconds;
            double before = _pace.Shown;
            _pace.Advance(seconds);
            // How hard the hole is feeding: 1 while the shown share moves as fast as the pace allows.
            float rate = Math.Clamp((float)(_pace.Shown - before) / seconds * FastestRunSeconds, 0, 1);
            _activity += (rate - _activity) * MathF.Min(1, seconds * 4);
            _hole.PullStrength = 1 + 2.5f * _activity;
            _hole.Advance(elapsed);
            Spawn(seconds);
            Fall(seconds);
            if (_pace.IsDone && _doneAt < 0)
            {
                _doneAt = _clock;
            }

            float since = _clock - _doneAt;
            if (_doneAt >= 0 && since >= FinalFlareSeconds && (_pieces.Count == 0 || since >= LongestDrainSeconds))
            {
                _done.TrySetResult();
            }
        }
    }

    public void Draw(CanvasDrawingSession session, Vector2 size)
    {
        lock (_gate)
        {
            EnsureResources(session);
            _center = _hole.Center(size);
            _orbitSize = new Vector2(size.X * OrbitShareX, size.Y * OrbitShareY);
            _diskRadius = _hole.DiskRadius(size);
            _shadow = _diskRadius * 0.2f;

            float shown = (float)_pace.Shown;
            var planetsBehind = new List<Planet>();
            var planetsFront = new List<Planet>();
            foreach (Planet planet in _planets)
            {
                (PositionOf(planet).Y < _center.Y ? planetsBehind : planetsFront).Add(planet);
            }

            foreach (Planet planet in planetsBehind)
            {
                DrawPlanet(session, planet, shown);
            }

            DrawPieces(session, behind: true);
            DrawFlare(session);
            _hole.Draw(session, size);
            foreach (Planet planet in planetsFront)
            {
                DrawPlanet(session, planet, shown);
            }

            DrawPieces(session, behind: false);
        }
    }

    private Vector2 PositionOf(Planet planet) =>
        _center + new Vector2(MathF.Cos(planet.Angle) * _orbitSize.X, MathF.Sin(planet.Angle) * _orbitSize.Y);

    // Each planet breaks up at its own pace round the shared one, but all of them are gone at 100%.
    private static float ErosionOf(Planet planet, float shown) => MathF.Pow(shown, planet.Pace);

    /// <summary>
    /// Where the breaking front stands on a planet, in its own frame: u runs toward the hole, v across. What is left
    /// is the part with u below the front; the front starts at the near edge and crosses to the far one, ragged.
    /// </summary>
    private float FrontAt(Planet planet, float erosion, float v)
    {
        // Placed so what is left of the disc matches what is left to do; the ragged bites, shallow at first and
        // last, swing round that line.
        float r = planet.Radius;
        float bite = MathF.Min(1, erosion * 6) * MathF.Min(1, (1 - erosion) * 4);
        float s = v / r;
        float ragged = 0.16f * MathF.Sin(s * 5.3f + planet.Phase)
            + 0.09f * MathF.Sin(s * 11.7f + planet.Phase * 2.1f + _clock * 1.3f)
            + 0.05f * MathF.Sin(s * 23f + planet.Phase * 3.7f - _clock * 2.1f);
        return r * (FrontFor(erosion) + 0.05f * (1 - erosion) - ragged * bite);
    }

    // The front's place (−1 far edge, 1 near edge) that leaves this share of the disc behind it.
    private static float FrontFor(float erosion)
    {
        float at = Math.Clamp(erosion, 0, 1) * (FrontTable.Length - 1);
        int low = (int)at;
        int high = Math.Min(low + 1, FrontTable.Length - 1);
        return FrontTable[low] + (FrontTable[high] - FrontTable[low]) * (at - low);
    }

    // Rocks and embers come off the breaking front, more of them the faster the cleanup goes.
    private void Spawn(float seconds)
    {
        float shown = (float)_pace.Shown;
        for (int i = 0; i < _planets.Length; i++)
        {
            Planet planet = _planets[i];
            float erosion = ErosionOf(planet, shown);
            if (erosion <= 0 || erosion >= 1)
            {
                continue;
            }

            _owed[i] += (PiecesPerSecondIdle + PiecesPerSecondFull * _activity) * planet.Radius / 20 * seconds;
            Vector2 position = PositionOf(planet);
            Vector2 toward = Vector2.Normalize(_center - position);
            var across = new Vector2(-toward.Y, toward.X);
            for (; _owed[i] >= 1; _owed[i]--)
            {
                if (_pieces.Count >= MaximumPieces)
                {
                    continue;
                }

                // A point on the front, inside what is left of the planet.
                float v = (_random.NextSingle() * 2 - 1) * planet.Radius;
                float u = FrontAt(planet, erosion, v);
                if (u * u + v * v > planet.Radius * planet.Radius)
                {
                    float chord = MathF.Sqrt(MathF.Max(0, planet.Radius * planet.Radius - v * v));
                    u = Math.Clamp(u, -chord, chord);
                }

                bool rock = _random.NextSingle() < RockShare;
                Vector2 start = position + toward * u + across * v;
                // Torn off toward the hole, with a sideways kick that makes the stream swirl.
                Vector2 velocity = toward * (25 + _random.NextSingle() * 40)
                    + across * planet.Swirl * (18 + _random.NextSingle() * 30)
                    + new Vector2(_random.NextSingle() - 0.5f, _random.NextSingle() - 0.5f) * 30;
                _pieces.Add(new Piece
                {
                    Position = start,
                    Velocity = velocity,
                    StartDistance = Vector2.Distance(start, _center),
                    Rock = rock,
                    Size = rock
                        ? (0.9f + MathF.Pow(_random.NextSingle(), 2.5f) * 3.6f) * planet.Radius / 20
                        : 0.8f + _random.NextSingle() * 1.6f,
                    Shape = _random.Next(RockShapes),
                    Spin = _random.NextSingle() * MathF.Tau,
                    Turn = (_random.NextSingle() - 0.5f) * 8,
                    Color = planet.RockColors[_random.Next(planet.RockColors.Length)],
                });
            }
        }
    }

    // Gravity toward the hole (softened near it), drag that turns orbits into spirals, and near the disk the pieces
    // settle into its plane. A piece is gone once it reaches the shadow.
    private void Fall(float seconds)
    {
        float reach = _orbitSize.X;
        float gravity = MathF.PI * MathF.PI / 8 * reach * reach * reach / (FallSeconds * FallSeconds);
        const float Softening = 400;
        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            Piece piece = _pieces[i];
            Vector2 offset = _center - piece.Position;
            float squared = offset.LengthSquared() + Softening;
            piece.Velocity += offset * (gravity / (squared * MathF.Sqrt(squared))) * seconds;
            piece.Velocity *= 1 - Drag * seconds;
            if (offset.Length() < _diskRadius)
            {
                piece.Velocity = new Vector2(piece.Velocity.X, piece.Velocity.Y * (1 - 4 * seconds));
                piece.Position = new Vector2(piece.Position.X, piece.Position.Y + offset.Y * 3 * seconds);
            }

            piece.Position += piece.Velocity * seconds;
            piece.Spin += piece.Turn * seconds;
            piece.Age += seconds;
            if (Vector2.Distance(piece.Position, _center) < _shadow * 1.05f || piece.Age > 8)
            {
                _pieces.RemoveAt(i);
            }
        }
    }

    // Rocks first, dark with an edge lit hot on the side facing the hole; then embers, added as light.
    private void DrawPieces(CanvasDrawingSession session, bool behind)
    {
        Matrix3x2 previous = session.Transform;
        foreach (Piece piece in _pieces)
        {
            if (!piece.Rock || piece.Position.Y < _center.Y != behind)
            {
                continue;
            }

            float heat = Heat(piece);
            float fade = Fade(piece);
            Vector2 toward = Vector2.Normalize(_center - piece.Position);
            CanvasGeometry shape = _rocks[piece.Shape];
            Matrix3x2 place = Matrix3x2.CreateScale(piece.Size) * Matrix3x2.CreateRotation(piece.Spin);
            session.Transform = place * Matrix3x2.CreateTranslation(piece.Position) * previous;
            session.FillGeometry(shape, WithAlpha(Mix(Fire, Hot, heat), (byte)(255 * fade)));
            session.Transform = place * Matrix3x2.CreateScale(0.9f) *
                Matrix3x2.CreateTranslation(piece.Position - toward * piece.Size * (0.16f - 0.1f * heat)) * previous;
            session.FillGeometry(shape, WithAlpha(Mix(piece.Color, Ember, heat * heat * 0.6f), (byte)(255 * fade)));
        }

        session.Transform = previous;
        CanvasBlend blend = session.Blend;
        session.Blend = CanvasBlend.Add;
        foreach (Piece piece in _pieces)
        {
            if (piece.Rock || piece.Position.Y < _center.Y != behind)
            {
                continue;
            }

            // A spark: a short streak along its way, with a faint halo.
            float heat = Heat(piece);
            float fade = Fade(piece);
            Color color = Mix(Ember, Hot, heat);
            float speed = piece.Velocity.Length();
            Vector2 tail = piece.Position - piece.Velocity / MathF.Max(1, speed) * MathF.Min(speed * 0.04f, 7);
            session.DrawLine(tail, piece.Position, WithAlpha(color, (byte)(60 * fade)), piece.Size * 1.8f);
            session.DrawLine(tail, piece.Position, WithAlpha(color, (byte)(170 * fade)), piece.Size * 0.6f);
            session.FillCircle(piece.Position, piece.Size * 1.4f, WithAlpha(color, (byte)(20 * fade)));
        }

        session.Blend = blend;
    }

    // Pieces heat toward white as they fall and fade into the shadow.
    private float Heat(Piece piece) =>
        Math.Clamp(1 - (Vector2.Distance(piece.Position, _center) - _shadow) / MathF.Max(1, piece.StartDistance - _shadow), 0, 1);

    private float Fade(Piece piece) =>
        Math.Clamp((Vector2.Distance(piece.Position, _center) - _shadow) / (_shadow * 0.8f), 0, 1) * MathF.Min(1, piece.Age * 6);

    // The disk glows while it feeds, and once more, brighter, when the last of the planets is gone.
    private void DrawFlare(CanvasDrawingSession session)
    {
        Glow(session, _center, _shadow * 3.4f, Mix(AccentLight, Fire, 0.45f), _activity * 0.6f);
        if (_doneAt >= 0)
        {
            float t = (_clock - _doneAt) / FinalFlareSeconds;
            Glow(session, _center, _shadow * (3 + 3 * t), Hot, MathF.Max(0, 1 - t) * 0.8f);
        }
    }

    private static void Glow(CanvasDrawingSession session, Vector2 center, float radius, Color color, float strength)
    {
        if (strength <= 0.01f)
        {
            return;
        }

        // Many faint discs add up to a soft glow without visible steps.
        for (int ring = GlowRings; ring >= 1; ring--)
        {
            float t = ring / (float)GlowRings;
            session.FillCircle(center, radius * t, WithAlpha(color, (byte)(12 * strength * (1.2f - t))));
        }
    }

    /// <summary>
    /// One planet: its noise-made surface turning slowly inside what is left of it, shaded away from the hole and
    /// darker toward its rim; along the breaking front a molten, glowing edge, and firelight on the rock behind it.
    /// </summary>
    private void DrawPlanet(CanvasDrawingSession session, Planet planet, float shown)
    {
        float erosion = ErosionOf(planet, shown);
        if (erosion >= 1)
        {
            return;
        }

        Vector2 position = PositionOf(planet);
        float r = planet.Radius;
        Vector2 toward = Vector2.Normalize(_center - position);
        var across = new Vector2(-toward.Y, toward.X);
        using CanvasGeometry body = Remaining(session, planet, position, toward, across, erosion);

        if (planet.Kind == PlanetKind.Ringed)
        {
            DrawRing(session, planet, position, front: false, erosion);
        }

        if (planet.Atmosphere is { } air)
        {
            float thin = 1 - MathF.Min(1, erosion * 1.6f);
            session.FillCircle(position, r * 1.16f, WithAlpha(air, (byte)(0x22 * thin)));
            session.FillCircle(position, r * 1.06f, WithAlpha(air, (byte)(0x40 * thin)));
        }

        using (session.CreateLayer(1, body))
        {
            // The surface: a texture twice as wide as the planet, so half of it shows, scrolled as it turns.
            float width = r * 4;
            float scroll = (_clock * SpinPerSecond * planet.SpinSign % 1 + 1) % 1;
            float left = position.X - r - scroll * width;
            for (int tile = 0; tile < 2; tile++)
            {
                session.DrawImage(planet.Texture!, new global::Windows.Foundation.Rect(left + tile * width, position.Y - r, width, r * 2));
            }

            _limb!.Center = position;
            _limb.RadiusX = r;
            _limb.RadiusY = r;
            session.FillCircle(position, r, _limb);
            _shade!.Center = position + toward * r * 0.45f;
            _shade.RadiusX = r * 1.45f;
            _shade.RadiusY = r * 1.45f;
            session.FillCircle(position, r, _shade);

            if (erosion > 0)
            {
                // Firelight from the molten front on the rock just behind it.
                Vector2 front = position + toward * FrontAt(planet, erosion, 0);
                CanvasBlend blend = session.Blend;
                session.Blend = CanvasBlend.Add;
                Glow(session, front, r * 0.9f, Fire, 1.6f);
                session.Blend = blend;
            }
        }

        if (erosion > 0)
        {
            DrawFront(session, planet, position, toward, across, erosion);
        }

        if (planet.Kind == PlanetKind.Ringed)
        {
            DrawRing(session, planet, position, front: true, erosion);
        }
    }

    // What is left of the planet: its disc cut along the ragged front.
    private CanvasGeometry Remaining(CanvasDrawingSession session, Planet planet, Vector2 position, Vector2 toward, Vector2 across, float erosion)
    {
        CanvasGeometry round = CanvasGeometry.CreateCircle(session, position, planet.Radius);
        if (erosion <= 0)
        {
            return round;
        }

        float r = planet.Radius;
        using var builder = new CanvasPathBuilder(session);
        const int Steps = 28;
        for (int i = 0; i <= Steps; i++)
        {
            float v = -r * 1.2f + 2.4f * r * i / Steps;
            Vector2 point = position + toward * FrontAt(planet, erosion, v) + across * v;
            if (i == 0)
            {
                builder.BeginFigure(point);
            }
            else
            {
                builder.AddLine(point);
            }
        }

        builder.AddLine(position - toward * r * 2 + across * r * 1.2f);
        builder.AddLine(position - toward * r * 2 - across * r * 1.2f);
        builder.EndFigure(CanvasFigureLoop.Closed);
        using CanvasGeometry kept = CanvasGeometry.CreatePath(builder);
        CanvasGeometry body = round.CombineWith(kept, Matrix3x2.Identity, CanvasGeometryCombine.Intersect);
        round.Dispose();
        return body;
    }

    // The breaking edge glows: a wide soft orange under a thin white-hot line, only where the planet still is.
    private void DrawFront(CanvasDrawingSession session, Planet planet, Vector2 position, Vector2 toward, Vector2 across, float erosion)
    {
        float r = planet.Radius;
        CanvasBlend blend = session.Blend;
        session.Blend = CanvasBlend.Add;
        const int Steps = 28;
        Vector2? last = null;
        for (int i = 0; i <= Steps; i++)
        {
            float v = -r + 2 * r * i / Steps;
            float u = FrontAt(planet, erosion, v);
            if (u * u + v * v > r * r)
            {
                last = null;
                continue;
            }

            Vector2 point = position + toward * u + across * v;
            if (last is { } from)
            {
                session.DrawLine(from, point, WithAlpha(Fire, 0x50), r * 0.28f);
                session.DrawLine(from, point, WithAlpha(Fire, 0xA0), r * 0.12f);
                session.DrawLine(from, point, WithAlpha(Hot, 0xE0), r * 0.045f);
            }

            last = point;
        }

        session.Blend = blend;
    }

    // The ring round the planet, tilted; its far half goes behind the body and its near half in front. It frays as
    // the planet goes: thinner and fainter.
    private static void DrawRing(CanvasDrawingSession session, Planet planet, Vector2 position, bool front, float erosion)
    {
        float r = planet.Radius;
        float left = 1 - erosion;
        Matrix3x2 previous = session.Transform;
        session.Transform = Matrix3x2.CreateRotation(-0.38f, position) * previous;
        var half = new global::Windows.Foundation.Rect(position.X - r * 3, front ? position.Y : position.Y - r * 3, r * 6, r * 3);
        using (CanvasGeometry clip = CanvasGeometry.CreateRectangle(session, half))
        using (session.CreateLayer(left, clip))
        {
            for (int band = 0; band < 5; band++)
            {
                float radius = r * (1.4f + band * 0.15f);
                Color color = band % 2 == 0 ? planet.RockColors[1] : planet.RockColors[0];
                session.DrawEllipse(position, radius, radius * 0.28f, WithAlpha(color, (byte)(band == 4 ? 0x50 : 0xA8)), r * 0.1f);
            }
        }

        session.Transform = previous;
    }

    private void EnsureResources(CanvasDrawingSession session)
    {
        if (_device == session.Device)
        {
            return;
        }

        _device = session.Device;
        _shade?.Dispose();
        _limb?.Dispose();
        // Night side: dark away from the hole, whose disk is the only light here.
        _shade = new CanvasRadialGradientBrush(session,
        [
            new CanvasGradientStop { Position = 0, Color = Color.FromArgb(0, 0, 0, 0) },
            new CanvasGradientStop { Position = 0.45f, Color = Color.FromArgb(0x18, 0x04, 0x02, 0x08) },
            new CanvasGradientStop { Position = 0.72f, Color = Color.FromArgb(0xB8, 0x04, 0x02, 0x08) },
            new CanvasGradientStop { Position = 1, Color = Color.FromArgb(0xF8, 0x04, 0x02, 0x08) },
        ]);
        // Limb darkening: a sphere looks darker toward its edge.
        _limb = new CanvasRadialGradientBrush(session,
        [
            new CanvasGradientStop { Position = 0, Color = Color.FromArgb(0, 0, 0, 0) },
            new CanvasGradientStop { Position = 0.7f, Color = Color.FromArgb(0x10, 0, 0, 0) },
            new CanvasGradientStop { Position = 1, Color = Color.FromArgb(0x90, 0, 0, 0) },
        ]);
        _rocks = Enumerable.Range(0, RockShapes).Select(i => RockShape(session, new Random(100 + i))).ToArray();
        foreach (Planet planet in _planets)
        {
            planet.Texture?.Dispose();
            planet.Texture = Surfaces.Make(session.Device, planet.Kind);
        }
    }

    // An irregular rock of radius about 1: six to nine corners at uneven distances.
    private static CanvasGeometry RockShape(CanvasDrawingSession session, Random random)
    {
        int corners = 6 + random.Next(4);
        using var builder = new CanvasPathBuilder(session);
        for (int i = 0; i < corners; i++)
        {
            float angle = i * MathF.Tau / corners + (random.NextSingle() - 0.5f) * 0.5f;
            float reach = 0.6f + random.NextSingle() * 0.45f;
            var point = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * reach;
            if (i == 0)
            {
                builder.BeginFigure(point);
            }
            else
            {
                builder.AddLine(point);
            }
        }

        builder.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(builder);
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Mix(Color from, Color to, float t) => Color.FromArgb(
        from.A,
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);

    private enum PlanetKind
    {
        GasGiant,
        Ringed,
        Ice,
        Lava,
        Ocean,
        Moon,
        Desert,
    }

    /// <summary>A rock or an ember torn off a planet, moving under the hole's pull.</summary>
    private sealed class Piece
    {
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public float StartDistance { get; init; }
        public bool Rock { get; init; }
        public float Size { get; init; }
        public int Shape { get; init; }
        public float Spin { get; set; }
        public float Turn { get; init; }
        public float Age { get; set; }
        public Color Color { get; init; }
    }

    private sealed class Planet
    {
        public PlanetKind Kind { get; private init; }
        public float Angle { get; private init; }
        public float Radius { get; private init; }
        public float SpinSign { get; private init; }
        public float Swirl { get; private init; }
        public float Pace { get; private init; }
        public float Phase { get; private init; }
        public Color? Atmosphere { get; private init; }
        public Color[] RockColors { get; private init; } = [];
        public CanvasRenderTarget? Texture { get; set; }

        public static Planet Make(PlanetKind kind, float angle, Random random)
        {
            (float radius, Color? air, Color[] rocks) = kind switch
            {
                PlanetKind.GasGiant => (26f, (Color?)null, new[] { Rgb(0x6E, 0x48, 0x2E), Rgb(0xC9, 0x9A, 0x64), Rgb(0x8E, 0x5B, 0x3A) }),
                PlanetKind.Ringed => (19f, null, new[] { Rgb(0x9A, 0x84, 0x58), Rgb(0xE0, 0xCC, 0x98), Rgb(0x7A, 0x66, 0x44) }),
                PlanetKind.Ice => (17f, Rgb(0xA8, 0xDC, 0xF4), new[] { Rgb(0x8C, 0xB8, 0xCC), Rgb(0xD8, 0xEE, 0xF8), Rgb(0x5E, 0x86, 0x9C) }),
                PlanetKind.Lava => (17f, null, new[] { Rgb(0x2A, 0x16, 0x12), Rgb(0x48, 0x24, 0x18), Rgb(0x1A, 0x0E, 0x0C) }),
                PlanetKind.Ocean => (21f, Rgb(0x6C, 0xB4, 0xFF), new[] { Rgb(0x4A, 0x56, 0x4A), Rgb(0x6E, 0x62, 0x4C), Rgb(0x3A, 0x40, 0x44) }),
                PlanetKind.Moon => (14f, null, new[] { Rgb(0x5E, 0x5C, 0x64), Rgb(0x8C, 0x8A, 0x94), Rgb(0x46, 0x44, 0x4C) }),
                _ => (18f, Rgb(0xE8, 0x9A, 0x6A), new[] { Rgb(0x7A, 0x36, 0x1E), Rgb(0xA8, 0x5A, 0x34), Rgb(0x5A, 0x28, 0x18) }),
            };

            return new Planet
            {
                Kind = kind,
                Angle = angle,
                Radius = radius,
                SpinSign = random.NextSingle() < 0.5f ? -1 : 1,
                Swirl = random.NextSingle() < 0.5f ? -1 : 1,
                Pace = 0.85f + random.NextSingle() * 0.3f,
                Phase = random.NextSingle() * MathF.Tau,
                Atmosphere = air,
                RockColors = rocks,
            };
        }
    }

    /// <summary>
    /// Planet surfaces made once from Win2D's turbulence noise: each layer is noise turned grey, then mapped between
    /// two colours and, for the upper layers, into an alpha mask (land, clouds, seams). Seamless across the width, so
    /// the surface can scroll as the planet turns.
    /// </summary>
    private static class Surfaces
    {
        public static CanvasRenderTarget Make(CanvasDevice device, PlanetKind kind)
        {
            var target = new CanvasRenderTarget(device, TextureWidth, TextureHeight, 96);
            using CanvasDrawingSession session = target.CreateDrawingSession();
            switch (kind)
            {
                case PlanetKind.GasGiant:
                    Layer(session, new Vector2(0.004f, 0.06f), 5, 1, Rgb(0xB0, 0x7A, 0x48), Rgb(0xEE, 0xDC, 0xB8));
                    Layer(session, new Vector2(0.003f, 0.035f), 3, 2, Rgb(0x7A, 0x46, 0x2A), Rgb(0x96, 0x5A, 0x36), 2.4f, -1.25f);
                    Layer(session, new Vector2(0.01f, 0.09f), 4, 3, Rgb(0xF4, 0xE8, 0xD0), Rgb(0xFF, 0xF4, 0xE2), 2.2f, -1.3f);
                    session.FillEllipse(178, 84, 15, 8, Color.FromArgb(0xC0, 0xB8, 0x58, 0x34));
                    session.DrawEllipse(178, 84, 15, 8, Color.FromArgb(0x70, 0xF0, 0xC8, 0xA0), 2);
                    break;
                case PlanetKind.Ringed:
                    Layer(session, new Vector2(0.003f, 0.05f), 4, 4, Rgb(0xC8, 0xAE, 0x74), Rgb(0xF2, 0xE4, 0xBE));
                    Layer(session, new Vector2(0.006f, 0.08f), 3, 5, Rgb(0xA8, 0x8C, 0x58), Rgb(0xB8, 0x9C, 0x66), 2.4f, -1.4f);
                    break;
                case PlanetKind.Ice:
                    Layer(session, new Vector2(0.018f, 0.018f), 5, 6, Rgb(0x92, 0xC2, 0xDA), Rgb(0xEC, 0xF8, 0xFE));
                    Layer(session, new Vector2(0.04f, 0.01f), 4, 7, Rgb(0x58, 0x8E, 0xB0), Rgb(0x6C, 0xA4, 0xC4), 3, -1.9f);
                    session.FillRectangle(0, 0, TextureWidth, 14, Color.FromArgb(0xD0, 0xF6, 0xFC, 0xFF));
                    session.FillRectangle(0, TextureHeight - 10, TextureWidth, 10, Color.FromArgb(0xB0, 0xF6, 0xFC, 0xFF));
                    break;
                case PlanetKind.Lava:
                    Layer(session, new Vector2(0.022f, 0.022f), 6, 8, Rgb(0x14, 0x0A, 0x08), Rgb(0x46, 0x26, 0x1A));
                    // Molten seams follow the noise's ridges, where its value is lowest.
                    Layer(session, new Vector2(0.02f, 0.02f), 4, 9, Rgb(0xFF, 0x4A, 0x10), Rgb(0xFF, 0xC8, 0x60), -7, 1.2f, TurbulenceEffectNoise.Turbulence);
                    break;
                case PlanetKind.Ocean:
                    Layer(session, new Vector2(0.014f, 0.014f), 4, 10, Rgb(0x0E, 0x30, 0x58), Rgb(0x22, 0x5E, 0x96));
                    Layer(session, new Vector2(0.011f, 0.011f), 6, 11, Rgb(0x3A, 0x6E, 0x34), Rgb(0xA8, 0x90, 0x5A), 10, -5.4f);
                    Layer(session, new Vector2(0.018f, 0.045f), 5, 12, Rgb(0xF2, 0xF6, 0xFF), Rgb(0xFF, 0xFF, 0xFF), 3.4f, -1.8f);
                    break;
                case PlanetKind.Moon:
                    Layer(session, new Vector2(0.03f, 0.03f), 6, 13, Rgb(0x66, 0x64, 0x6E), Rgb(0xBC, 0xBA, 0xC4));
                    var random = new Random(14);
                    for (int i = 0; i < 16; i++)
                    {
                        var at = new Vector2(random.NextSingle() * TextureWidth, 12 + random.NextSingle() * (TextureHeight - 24));
                        float size = 3 + MathF.Pow(random.NextSingle(), 2) * 12;
                        session.FillCircle(at, size, Color.FromArgb(0x70, 0x30, 0x2E, 0x36));
                        session.DrawCircle(at + new Vector2(-size * 0.2f, -size * 0.2f), size, Color.FromArgb(0x60, 0xE0, 0xDE, 0xE8), 1.2f);
                    }

                    break;
                default:
                    Layer(session, new Vector2(0.02f, 0.02f), 6, 15, Rgb(0x86, 0x3A, 0x1E), Rgb(0xD4, 0x7E, 0x48));
                    Layer(session, new Vector2(0.012f, 0.012f), 4, 16, Rgb(0x4E, 0x22, 0x14), Rgb(0x62, 0x2C, 0x1A), 4, -2.5f);
                    session.FillRectangle(0, 0, TextureWidth, 8, Color.FromArgb(0xC0, 0xF4, 0xEE, 0xEA));
                    break;
            }

            return target;
        }

        // One layer: noise → grey → colour between low and high, alpha = alphaScale·grey + alphaOffset.
        private static void Layer(
            CanvasDrawingSession session, Vector2 frequency, int octaves, int seed, Color low, Color high,
            float alphaScale = 0, float alphaOffset = 1, TurbulenceEffectNoise noise = TurbulenceEffectNoise.FractalSum)
        {
            using var turbulence = new TurbulenceEffect
            {
                Frequency = frequency,
                Octaves = octaves,
                Seed = seed,
                Noise = noise,
                Tileable = true,
                Size = new Vector2(TextureWidth, TextureHeight),
            };
            using var grey = new ColorMatrixEffect
            {
                Source = turbulence,
                AlphaMode = CanvasAlphaMode.Straight,
                ClampOutput = true,
                ColorMatrix = new Matrix5x4 { M11 = 1, M12 = 1, M13 = 1, M54 = 1 },
            };
            using var colored = new ColorMatrixEffect
            {
                Source = grey,
                ClampOutput = true,
                ColorMatrix = new Matrix5x4
                {
                    M11 = (high.R - low.R) / 255f,
                    M12 = (high.G - low.G) / 255f,
                    M13 = (high.B - low.B) / 255f,
                    M14 = alphaScale,
                    M51 = low.R / 255f,
                    M52 = low.G / 255f,
                    M53 = low.B / 255f,
                    M54 = alphaOffset,
                },
            };
            session.DrawImage(colored);
        }
    }
}
