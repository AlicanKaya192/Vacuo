using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace Vacuo.App.Visuals;

/// <summary>
/// One subfolder in the Düzen preview: its <paramref name="Name"/>, the chip's <paramref name="Label"/>, how many
/// files go there and their names, whether Vacuo makes it, and the icon it will get.
/// </summary>
internal sealed record FlowTarget(string Name, string Label, int Count, bool IsNew, IReadOnlyList<string> Files, string? IconFile = null);

/// <summary>
/// The Düzen preview as motion (B-161 dilim 4, Aşama 3i): the folder as a black hole on the left, its
/// subfolders on the right with their icons, and files flowing along a curve to each, now and then one
/// with its name. <see cref="BeginRun"/> and <see cref="LaunchFiles"/> play the move itself: the hole
/// spins up, every file flies with its name to its folder, the folder counts it in and gets a tick; a file
/// that could not move fades halfway and falls back. <see cref="PlayUndo"/> sends them back into the hole.
/// Built on the UI thread, advanced and drawn on the Win2D game-loop thread; one lock keeps them apart.
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
    /// <summary>The chip icon's size in DIPs; the view loads the frame drawn for it.</summary>
    public const float IconSize = 16f;

    private const float SourceRadius = 22f;
    private const int TokensPerFile = 2;
    private const int MaximumTokensPerLane = 10;
    private const int DustPerLane = 6;
    private const float CyclesPerSecond = 0.28f;

    // A name rides along one lane at a time, so the strip says which file goes where.
    private const float TagSeconds = 2.4f;
    private const int TagCharacters = 22;

    // The folder's name and file count sit in two lines under the hole, short enough to stay inside the strip.
    private const int SourceCharacters = 18;

    // The run: the hole spins up, then the files leave one after another and fly for FlightSeconds.
    private const float SpinUpSeconds = 0.45f;
    private const float FlightSeconds = 0.85f;
    private const float LongestLaunchWindow = 1.6f;
    private const float PulseSeconds = 0.5f;
    private const float SettleSeconds = 0.6f;
    private const int NamedFlightsAtMost = 14;

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
    private static readonly Color Warning = Color.FromArgb(0xFF, 0xE9, 0xB9, 0x64);
    private static readonly Color CurveColor = Color.FromArgb(0x38, 0x9D, 0x7C, 0xFF);

    private static readonly CanvasTextFormat LabelFormat = new()
    {
        FontSize = 12,
        HorizontalAlignment = CanvasHorizontalAlignment.Center,
        VerticalAlignment = CanvasVerticalAlignment.Center,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };

    private static readonly CanvasTextFormat TagFormat = new()
    {
        FontSize = 10.5f,
        HorizontalAlignment = CanvasHorizontalAlignment.Center,
        VerticalAlignment = CanvasVerticalAlignment.Center,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };

    private readonly object _gate = new();
    private readonly string _sourceName;
    private readonly string _sourceCount;
    private readonly Vector2 _source;
    private readonly Lane[] _lanes;
    private readonly Token[] _tokens;
    private readonly List<Flight> _flights = [];
    private float _diskAngle;
    private float _tagClock;
    private int _tagTurn;
    private Mode _mode = Mode.Preview;
    private float _runClock;
    private float _launchAt = -1;
    private float _doneAt = -1;
    private TaskCompletionSource? _finished;

    public FlowMap(string sourceName, string sourceCount, IReadOnlyList<FlowTarget> targets, Vector2 size)
    {
        _sourceName = Shorten(sourceName, SourceCharacters);
        _sourceCount = sourceCount;
        _source = new Vector2(SourceX, size.Y / 2);
        // One column of equal chips, spread evenly between the edge margins; room for the icon when any has one.
        bool withIcons = targets.Any(target => target.IconFile is not null);
        float width = targets.Count == 0 ? 0 : targets.Max(target => target.Label.Length) * ChipCharacterWidth + ChipPadding + (withIcons ? IconSize + 6 : 0);
        float step = (size.Y - 2 * EdgeMargin) / Math.Max(1, targets.Count);
        _lanes = targets.Select((target, index) =>
        {
            var chip = new Vector2(size.X - TargetMargin - width, EdgeMargin + step * (index + 0.5f) - ChipHeight / 2);
            return new Lane(target, chip, width, new Vector2(chip.X, chip.Y + ChipHeight / 2), withIcons);
        }).ToArray();

        var tokens = new List<Token>();
        for (int lane = 0; lane < _lanes.Length; lane++)
        {
            int files = Math.Min(MaximumTokensPerLane, Math.Max(1, _lanes[lane].Target.Count * TokensPerFile));
            for (int i = 0; i < files + DustPerLane; i++)
            {
                tokens.Add(new Token
                {
                    Lane = lane,
                    Phase = Random.Shared.NextSingle(),
                    Speed = CyclesPerSecond * (0.75f + Random.Shared.NextSingle() * 0.5f),
                    Drift = (Random.Shared.NextSingle() - 0.5f) * 10f,
                    IsFile = i < files,
                });
            }
        }

        _tokens = tokens.ToArray();
    }

    /// <summary>The icon drawn in a chip; loaded by the view on the UI thread, used here when it arrives.</summary>
    public void SetIcon(int lane, CanvasBitmap icon)
    {
        lock (_gate)
        {
            _lanes[lane].Icon = icon;
        }
    }

    /// <summary>The user said yes: the hole spins up and the stream thins out while the files are moved.</summary>
    public void BeginRun()
    {
        lock (_gate)
        {
            _mode = Mode.Run;
            _runClock = 0;
            _doneAt = -1;
            _flights.Clear();
            _finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// The move is done: every file flies to its folder with its name; those in <paramref name="failed"/>
    /// (file names) fall back. Completes once every folder has counted its files in.
    /// </summary>
    public Task LaunchFiles(IReadOnlySet<string> failed)
    {
        lock (_gate)
        {
            if (_mode != Mode.Run || _finished is null)
            {
                return Task.CompletedTask;
            }

            List<(int Lane, string Name)> files = Interleaved();
            float gap = files.Count <= 1 ? 0 : Math.Min(0.25f, LongestLaunchWindow / (files.Count - 1));
            _launchAt = Math.Max(_runClock, SpinUpSeconds);
            int named = Math.Max(1, (int)Math.Ceiling(files.Count / (float)NamedFlightsAtMost));
            for (int i = 0; i < files.Count; i++)
            {
                _flights.Add(new Flight
                {
                    Lane = files[i].Lane,
                    Name = i % named == 0 ? Shorten(files[i].Name) : null,
                    Start = _launchAt + i * gap,
                    Failed = failed.Contains(files[i].Name),
                    Drift = (Random.Shared.NextSingle() - 0.5f) * 8f,
                });
            }

            _doneAt = _launchAt + (files.Count <= 1 ? 0 : (files.Count - 1) * gap) + FlightSeconds + SettleSeconds;
            return _finished.Task;
        }
    }

    /// <summary>Undo: the files leave their folders and fall back into the hole. Completes when the last is in.</summary>
    public Task PlayUndo()
    {
        lock (_gate)
        {
            _mode = Mode.Undo;
            _runClock = 0;
            _flights.Clear();
            _finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (Lane lane in _lanes)
            {
                lane.Arrived = lane.Target.Files.Count;
            }

            List<(int Lane, string Name)> files = Interleaved();
            float gap = files.Count <= 1 ? 0 : Math.Min(0.2f, LongestLaunchWindow / (files.Count - 1));
            for (int i = 0; i < files.Count; i++)
            {
                _flights.Add(new Flight { Lane = files[i].Lane, Start = 0.15f + i * gap, Drift = (Random.Shared.NextSingle() - 0.5f) * 8f });
            }

            _launchAt = 0.15f;
            _doneAt = 0.15f + (files.Count <= 1 ? 0 : (files.Count - 1) * gap) + FlightSeconds + 0.3f;
            return _finished.Task;
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        float seconds = (float)Math.Min(elapsed.TotalSeconds, 0.1);
        lock (_gate)
        {
            float spin = _mode == Mode.Preview ? 1f : 1f + 3f * Math.Min(1f, _runClock / SpinUpSeconds);
            _diskAngle = (_diskAngle + DiskTurnsPerSecond * spin * MathF.Tau * seconds) % MathF.Tau;
            for (int i = 0; i < _tokens.Length; i++)
            {
                ref Token token = ref _tokens[i];
                token.Phase += token.Speed * seconds;
                if (token.Phase >= 1f)
                {
                    token.Phase -= 1f;
                }
            }

            _tagClock += seconds;
            if (_tagClock >= TagSeconds)
            {
                _tagClock -= TagSeconds;
                _tagTurn++;
            }

            if (_mode == Mode.Preview)
            {
                return;
            }

            _runClock += seconds;
            foreach (Flight flight in _flights)
            {
                if (!flight.Landed && _runClock >= flight.Start + FlightSeconds)
                {
                    flight.Landed = true;
                    if (!flight.Failed)
                    {
                        Lane lane = _lanes[flight.Lane];
                        lane.Arrived += _mode == Mode.Run ? 1 : -1;
                        lane.PulseAt = _runClock;
                    }
                }
            }

            if (_doneAt >= 0 && _runClock >= _doneAt)
            {
                _finished?.TrySetResult();
            }
        }
    }

    public void Draw(CanvasDrawingSession session)
    {
        lock (_gate)
        {
            // While files fly, the idle stream steps back so the flights stand out.
            float calm = _mode == Mode.Preview ? 1f : Math.Max(0.15f, 1f - _runClock / SpinUpSeconds);
            foreach (Lane lane in _lanes)
            {
                using var builder = new CanvasPathBuilder(session);
                builder.BeginFigure(_source);
                (Vector2 first, Vector2 second) = Controls(lane);
                builder.AddCubicBezier(first, second, lane.End);
                builder.EndFigure(CanvasFigureLoop.Open);
                using CanvasGeometry curve = CanvasGeometry.CreatePath(builder);
                session.DrawGeometry(curve, _mode == Mode.Preview ? CurveColor : WithAlpha(Accent, 0x60), 1.5f);
            }

            DrawTokens(session, calm);
            if (_mode == Mode.Preview)
            {
                DrawTag(session);
            }

            DrawSource(session);
            session.DrawText(_sourceName, new Vector2(_source.X, _source.Y + SourceRadius + 11), Ink, LabelFormat);
            session.DrawText(_sourceCount, new Vector2(_source.X, _source.Y + SourceRadius + 26), WithAlpha(Ink, 0xA0), TagFormat);
            foreach (Lane lane in _lanes)
            {
                DrawChip(session, lane);
            }

            DrawFlights(session);
        }
    }

    // The idle stream: small files with a short trail, and fine dust between them.
    private void DrawTokens(CanvasDrawingSession session, float calm)
    {
        foreach (Token token in _tokens)
        {
            Lane lane = _lanes[token.Lane];
            float spread = MathF.Sin(token.Phase * MathF.PI);
            float alpha = MathF.Min(1f, spread * 2.2f) * calm;
            if (!token.IsFile)
            {
                session.FillCircle(PointOn(lane, token.Phase, token.Drift), 1.2f, WithAlpha(Accent, (byte)(200 * alpha)));
                continue;
            }

            for (int step = 3; step >= 1; step--)
            {
                float behind = token.Phase - step * 0.018f;
                if (behind > 0)
                {
                    session.FillCircle(PointOn(lane, behind, token.Drift), 1.6f - step * 0.3f, WithAlpha(Accent, (byte)(90 * alpha / step)));
                }
            }

            DrawFile(session, PointOn(lane, token.Phase, token.Drift), WithAlpha(AccentLight, (byte)(255 * alpha)), 1f);
        }
    }

    // A name rides along one lane at a time, taking turns across the lanes and their files.
    private void DrawTag(CanvasDrawingSession session)
    {
        List<(int Lane, string Name)> files = Interleaved();
        if (files.Count == 0)
        {
            return;
        }

        (int laneIndex, string name) = files[_tagTurn % files.Count];
        float t = _tagClock / TagSeconds;
        float alpha = t < 0.15f ? t / 0.15f : t > 0.8f ? (1 - t) / 0.2f : 1f;
        Lane lane = _lanes[laneIndex];
        Vector2 point = PointOn(lane, 0.08f + 0.84f * t, 0);
        DrawFile(session, point, WithAlpha(Ink, (byte)(255 * alpha)), 1.3f);
        DrawNameTag(session, Shorten(name), point, alpha);
    }

    private void DrawFlights(CanvasDrawingSession session)
    {
        foreach (Flight flight in _flights)
        {
            float t = (_runClock - flight.Start) / FlightSeconds;
            if (t <= 0 || flight.Landed && !flight.Failed)
            {
                continue;
            }

            Lane lane = _lanes[flight.Lane];
            if (_mode == Mode.Undo)
            {
                // Back along the curve, shrinking as the hole pulls it in.
                float back = 1 - EaseInOut(Math.Min(1f, t));
                float shrink = 0.4f + 0.6f * back;
                DrawFile(session, PointOn(lane, back, flight.Drift), WithAlpha(AccentLight, (byte)(255 * Math.Min(1f, back * 3))), 1.3f * shrink);
                continue;
            }

            float along;
            float alpha = 1f;
            Color color = AccentLight;
            if (flight.Failed)
            {
                // Halfway out, then back into the hole, fading in the warning colour.
                along = t < 0.5f ? EaseInOut(t * 2) * 0.5f : 0.5f * (1 - EaseInOut((t - 0.5f) * 2));
                alpha = t < 0.5f ? 1f : Math.Max(0f, 1 - (t - 0.5f) * 2);
                color = t < 0.4f ? AccentLight : Warning;
                if (t >= 1)
                {
                    continue;
                }
            }
            else
            {
                along = EaseInOut(Math.Min(1f, t));
            }

            Vector2 point = PointOn(lane, along, flight.Drift);
            for (int step = 4; step >= 1; step--)
            {
                float behind = along - step * 0.025f;
                if (behind > 0)
                {
                    session.FillCircle(PointOn(lane, behind, flight.Drift), 2.2f - step * 0.35f, WithAlpha(Accent, (byte)(140 * alpha / step)));
                }
            }

            DrawFile(session, point, WithAlpha(color, (byte)(255 * alpha)), 1.4f);
            if (flight.Name is not null)
            {
                DrawNameTag(session, flight.Name, point, alpha * MathF.Min(1f, MathF.Sin(MathF.Min(1f, t) * MathF.PI) * 2.5f));
            }
        }
    }

    // A file as a small page with a folded corner.
    private static void DrawFile(CanvasDrawingSession session, Vector2 center, Color color, float scale)
    {
        float w = 4.2f * scale;
        float h = 5.4f * scale;
        float fold = 1.6f * scale;
        using var builder = new CanvasPathBuilder(session);
        builder.BeginFigure(center.X - w / 2, center.Y - h / 2);
        builder.AddLine(center.X + w / 2 - fold, center.Y - h / 2);
        builder.AddLine(center.X + w / 2, center.Y - h / 2 + fold);
        builder.AddLine(center.X + w / 2, center.Y + h / 2);
        builder.AddLine(center.X - w / 2, center.Y + h / 2);
        builder.EndFigure(CanvasFigureLoop.Closed);
        using CanvasGeometry page = CanvasGeometry.CreatePath(builder);
        session.FillGeometry(page, color);
    }

    private static void DrawNameTag(CanvasDrawingSession session, string name, Vector2 under, float alpha)
    {
        if (alpha <= 0.02f)
        {
            return;
        }

        float width = name.Length * 6.1f + 12;
        var box = new global::Windows.Foundation.Rect(under.X - width / 2, under.Y - 22, width, 15);
        session.FillRoundedRectangle(box, 7.5f, 7.5f, WithAlpha(Surface, (byte)(230 * alpha)));
        session.DrawRoundedRectangle(box, 7.5f, 7.5f, WithAlpha(Accent, (byte)(150 * alpha)), 1f);
        session.DrawText(name, new Vector2(under.X, under.Y - 14.5f), WithAlpha(Ink, (byte)(255 * alpha)), TagFormat);
    }

    private void DrawChip(CanvasDrawingSession session, Lane lane)
    {
        var box = new global::Windows.Foundation.Rect(lane.Chip.X, lane.Chip.Y, lane.Width, ChipHeight);
        float pulse = lane.PulseAt < 0 ? 0 : Math.Max(0f, 1 - (_runClock - lane.PulseAt) / PulseSeconds);
        if (pulse > 0)
        {
            // A ring that grows out of the chip and fades, as a file lands.
            float grow = (1 - pulse) * 5;
            var glow = new global::Windows.Foundation.Rect(box.X - grow, box.Y - grow, box.Width + 2 * grow, box.Height + 2 * grow);
            session.DrawRoundedRectangle(glow, ChipHeight / 2 + grow, ChipHeight / 2 + grow, WithAlpha(AccentLight, (byte)(200 * pulse)), 1.5f);
        }

        session.FillRoundedRectangle(box, ChipHeight / 2, ChipHeight / 2, pulse > 0 ? Mix(Surface, Accent, 0.25f * pulse) : Surface);
        session.DrawRoundedRectangle(box, ChipHeight / 2, ChipHeight / 2, lane.Target.IsNew ? Accent : Line, 1.2f);
        if (lane.Target.IsNew)
        {
            // The "new folder" mark: a small lit dot on the chip's top edge.
            session.FillCircle(new Vector2(lane.Chip.X + lane.Width - ChipHeight / 2, lane.Chip.Y), 2.6f, AccentLight);
        }

        float textLeft = lane.Chip.X;
        if (lane.WithIcons)
        {
            if (lane.Icon is { } icon)
            {
                session.DrawImage(icon, new global::Windows.Foundation.Rect(lane.Chip.X + 8, lane.Chip.Y + (ChipHeight - IconSize) / 2, IconSize, IconSize));
            }

            textLeft += IconSize + 6;
        }

        string label = lane.Target.Label;
        if (_mode == Mode.Run && lane.Target.Files.Count > 0)
        {
            label = $"{lane.Target.Name} · {lane.Arrived}/{lane.Target.Files.Count}";
        }

        float right = lane.Chip.X + lane.Width;
        bool done = _mode == Mode.Run && _doneAt >= 0 && lane.Arrived == lane.Target.Files.Count && lane.Arrived > 0;
        session.DrawText(label, new Vector2((textLeft + right) / 2 - (done ? 6 : 0), lane.Chip.Y + ChipHeight / 2), Ink, LabelFormat);
        if (done)
        {
            // A tick once the folder holds all its files.
            float x = right - 16;
            float y = lane.Chip.Y + ChipHeight / 2;
            using var builder = new CanvasPathBuilder(session);
            builder.BeginFigure(x - 3.5f, y);
            builder.AddLine(x - 1, y + 2.6f);
            builder.AddLine(x + 4, y - 3);
            builder.EndFigure(CanvasFigureLoop.Open);
            using CanvasGeometry tick = CanvasGeometry.CreatePath(builder);
            session.DrawGeometry(tick, AccentLight, 1.8f, new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round });
        }
    }

    /// <summary>
    /// The folder as a black hole: a soft glow, the far half of the disk, its image bent around the shadow, the
    /// shadow with its photon ring, then the near half of the disk crossing in front. During a run it burns brighter.
    /// </summary>
    private void DrawSource(CanvasDrawingSession session)
    {
        float burn = _mode == Mode.Preview ? 0 : Math.Min(1f, _runClock / SpinUpSeconds) * (_doneAt >= 0 && _runClock > _doneAt - SettleSeconds ? 0.5f : 1f);
        for (int ring = 1; ring <= HaloRings; ring++)
        {
            session.FillCircle(_source, ShadowRadius + (SourceRadius + 6 * burn - ShadowRadius) * ring / HaloRings, WithAlpha(Accent, (byte)(0x07 + 0x06 * burn)));
        }

        DrawDisk(session, farSide: true, burn);
        DrawLensedDisk(session);
        session.FillCircle(_source, ShadowRadius, Void);
        session.DrawCircle(_source, ShadowRadius + 0.6f, WithAlpha(AccentLight, 0xD0), 1f);
        DrawDisk(session, farSide: false, burn);
    }

    /// <summary>
    /// The disk as rings of glowing gas, brightest inside. The right side turns toward the viewer and shines
    /// brighter; inner rings turn faster, so the bright clumps shear as they go round.
    /// </summary>
    private void DrawDisk(CanvasDrawingSession session, bool farSide, float burn)
    {
        for (int row = 0; row < DiskRows; row++)
        {
            float t = row / (float)(DiskRows - 1);
            float radius = DiskInnerRadius + (DiskOuterRadius - DiskInnerRadius) * t;
            float turn = _diskAngle * MathF.Pow(DiskInnerRadius / radius, 1.5f);
            float fade = MathF.Min(1f, (0.15f + 0.85f * (1 - t) * (1 - t)) * (1 + 0.6f * burn));
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

    // Every lane's files, one from each lane in turn, so the flights and the names spread over all folders.
    private List<(int Lane, string Name)> Interleaved()
    {
        var files = new List<(int, string)>();
        int longest = _lanes.Length == 0 ? 0 : _lanes.Max(lane => lane.Target.Files.Count);
        for (int i = 0; i < longest; i++)
        {
            for (int lane = 0; lane < _lanes.Length; lane++)
            {
                if (i < _lanes[lane].Target.Files.Count)
                {
                    files.Add((lane, _lanes[lane].Target.Files[i]));
                }
            }
        }

        return files;
    }

    private Vector2 PointOn(Lane lane, float t, float drift)
    {
        (Vector2 first, Vector2 second) = Controls(lane);
        Vector2 point = Bezier(_source, first, second, lane.End, t);
        // Spread out in the middle of the curve, gathered at both ends.
        point.Y += drift * MathF.Sin(t * MathF.PI);
        return point;
    }

    private static string Shorten(string name, int characters = TagCharacters) =>
        name.Length <= characters ? name : name[..(characters - 1)] + "…";

    private static float EaseInOut(float t) => t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Mix(Color from, Color to, float t) => Color.FromArgb(
        0xFF,
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));

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

    private enum Mode
    {
        Preview,
        Run,
        Undo,
    }

    private sealed class Lane(FlowTarget target, Vector2 chip, float width, Vector2 end, bool withIcons)
    {
        public FlowTarget Target { get; } = target;
        public Vector2 Chip { get; } = chip;
        public float Width { get; } = width;
        public Vector2 End { get; } = end;
        public bool WithIcons { get; } = withIcons;
        public CanvasBitmap? Icon { get; set; }
        public int Arrived { get; set; }
        public float PulseAt { get; set; } = -1;
    }

    private sealed class Flight
    {
        public int Lane { get; init; }
        public string? Name { get; init; }
        public float Start { get; init; }
        public bool Failed { get; init; }
        public float Drift { get; init; }
        public bool Landed { get; set; }
    }

    private struct Token
    {
        public int Lane;
        public float Phase;
        public float Speed;
        public float Drift;
        public bool IsFile;
    }
}
