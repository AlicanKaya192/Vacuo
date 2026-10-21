using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Vacuo.App.Screens;

/// <summary>
/// The bar that follows a removal (Alican, 21 October 2026): filled by how much is done, or, when no step is
/// known, a segment sweeping across. With Windows animations off the sweep stands still at half opacity
/// (vacuo-kurallar 9). Hidden until shown.
/// </summary>
public sealed partial class VoidProgress : UserControl
{
    private const double SweepShare = 0.3;
    private const double StillSweepOpacity = 0.5;
    private bool _animate = true;
    private bool _sweeping;

    public VoidProgress()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    internal void Initialize(bool animationsEnabled) => _animate = animationsEnabled;

    /// <param name="fraction">0 to 1.</param>
    internal void ShowFraction(double fraction)
    {
        StopSweep();
        Fill.Visibility = Visibility.Visible;
        FillScale.ScaleX = Math.Clamp(fraction, 0, 1);
        Visibility = Visibility.Visible;
    }

    internal void ShowSweep()
    {
        Fill.Visibility = Visibility.Collapsed;
        Sweep.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
        _sweeping = true;
        PlaceSweep();
    }

    internal void Hide()
    {
        StopSweep();
        Visibility = Visibility.Collapsed;
    }

    private void StopSweep()
    {
        _sweeping = false;
        SweepMotion.Stop();
        Sweep.Visibility = Visibility.Collapsed;
    }

    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The sweep moves outside the track at both ends; only the part inside shows.
        Track.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        if (_sweeping)
        {
            PlaceSweep();
        }
    }

    // The segment runs from just off the left edge to just off the right one, so it enters and leaves smoothly.
    private void PlaceSweep()
    {
        double width = Track.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        Sweep.Width = width * SweepShare;
        if (!_animate)
        {
            Sweep.Width = width;
            Sweep.Opacity = StillSweepOpacity;
            return;
        }

        var run = (DoubleAnimation)SweepMotion.Children[0];
        run.From = -Sweep.Width;
        run.To = width;
        SweepMotion.Begin();
    }
}
