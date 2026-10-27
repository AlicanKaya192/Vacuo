using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vacuo.App.Localization;
using Vacuo.App.Visuals;
using Vacuo.Core.Disk;

namespace Vacuo.App.Screens;

/// <summary>
/// The cleanup stage (Aşama 3j): while a cleanup runs, the black hole eats its seven planets as the work goes on,
/// with the share shown under it. It only plays where things move at all; otherwise the screen's own bar and
/// line say the same, and they keep doing so underneath, for a screen reader too.
/// </summary>
public sealed partial class CleanupStage : UserControl
{
    // The result never waits longer than this for the last planet, even if the scene stopped drawing.
    private static readonly TimeSpan WaitAtMost = TimeSpan.FromSeconds(4.5);
    private const double Percent = 100;

    private Localizer _localizer = null!;
    private bool _animationsEnabled;
    private volatile DevourScene? _scene;
    private DispatcherQueueTimer? _ticker;

    public CleanupStage()
    {
        InitializeComponent();
    }

    internal void Initialize(Localizer localizer, bool animationsEnabled)
    {
        _localizer = localizer;
        _animationsEnabled = animationsEnabled;
    }

    /// <summary>Stops Win2D's game-loop thread before the window goes away.</summary>
    internal void Shutdown() => Scene.RemoveFromVisualTree();

    /// <summary>Shows the stage; false when it does not play (animation effects off, or a contrast theme).</summary>
    internal bool Begin(string title)
    {
        if (!_animationsEnabled || ContrastMode.IsOn)
        {
            return false;
        }

        _scene = new DevourScene();
        TitleText.Text = title;
        CountText.Text = string.Empty;
        ShowPercent(0);
        Visibility = Visibility.Visible;
        Scene.Paused = false;
        // The share under the hole follows the scene, not the work, so the number and the planets agree.
        _ticker = DispatcherQueue.CreateTimer();
        _ticker.Interval = TimeSpan.FromMilliseconds(100);
        _ticker.Tick += (_, _) => ShowPercent(_scene?.Shown ?? 0);
        _ticker.Start();
        return true;
    }

    internal void Report(MoveProgress report)
    {
        _scene?.Report(report.Fraction);
        CountText.Text = $"{report.Done} / {report.Total}";
    }

    /// <summary>The work is over: the rest of the planets go in, then the stage closes.</summary>
    internal async Task FinishAsync()
    {
        if (_scene is not { } scene)
        {
            return;
        }

        scene.Finish();
        await Task.WhenAny(scene.Done, Task.Delay(WaitAtMost));
        Close();
    }

    /// <summary>Takes the stage away at once; nothing happens when it is not showing.</summary>
    internal void Close()
    {
        _ticker?.Stop();
        _ticker = null;
        Scene.Paused = true;
        _scene = null;
        Visibility = Visibility.Collapsed;
    }

    private void ShowPercent(double shown) =>
        PercentText.Text = _localizer.Format("CleanupPercentFormat", (int)Math.Round(shown * Percent));

    private void OnSceneUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args) =>
        _scene?.Advance(args.Timing.ElapsedTime);

    private void OnSceneDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args) =>
        _scene?.Draw(args.DrawingSession, sender.Size.ToVector2());
}
