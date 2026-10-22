using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Vacuo.App.Visuals;
using Windows.System;
using Windows.UI;

namespace Vacuo.App.Screens;

/// <summary>One line of what is about to go: an edge colour, a name, a detail and, when known, a size.</summary>
internal sealed record ConfirmItem(Color Edge, string Name, string Detail, string? Size);

/// <summary>What a confirm step says: the texts, the button labels and the parts in between.</summary>
internal sealed record ConfirmRequest(string Title, string Summary, string ConfirmText, string CancelText, IReadOnlyList<UIElement> Parts);

/// <summary>
/// The VOID confirm step shared by the screens: the black hole above what is about to go,
/// Cancel and the action. Cancel has the focus, so Enter (like Esc) keeps everything where it is.
/// </summary>
public sealed partial class ConfirmCard : UserControl
{
    private const string WarningGlyph = "";
    private const string NoteGlyph = "";

    private readonly BlackHoleScene _hole = new();
    private bool _animationsEnabled;
    private TaskCompletionSource<bool>? _answer;

    public ConfirmCard()
    {
        InitializeComponent();
    }

    internal void Initialize(bool animationsEnabled) => _animationsEnabled = animationsEnabled;

    /// <summary>Stops Win2D's game-loop thread before the window goes away.</summary>
    internal void Shutdown() => Hole.RemoveFromVisualTree();

    /// <summary>Shows the card and returns true only when the user picks the action.</summary>
    internal async Task<bool> AskAsync(ConfirmRequest request)
    {
        TitleText.Text = request.Title;
        SummaryText.Text = request.Summary;
        ConfirmButton.Content = request.ConfirmText;
        CancelButton.Content = request.CancelText;
        Items.Children.Clear();
        foreach (UIElement part in request.Parts)
        {
            Items.Children.Add(part);
        }

        _answer = new TaskCompletionSource<bool>();
        Visibility = Visibility.Visible;
        Hole.Paused = !_animationsEnabled;
        if (!_animationsEnabled)
        {
            Hole.Invalidate();
        }

        CancelButton.Focus(FocusState.Programmatic);
        bool confirmed = await _answer.Task;
        Hole.Paused = true;
        Visibility = Visibility.Collapsed;
        return confirmed;
    }

    /// <summary>Closes an open card as if Cancel were picked; nothing happens when none is open.</summary>
    internal void Cancel() => _answer?.TrySetResult(false);

    internal static Border Warning(string text)
    {
        var warning = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        warning.Children.Add(new FontIcon { Glyph = WarningGlyph, FontSize = 16, Foreground = Resource<Brush>("WarningBrush") });
        warning.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440,
            Foreground = Resource<Brush>("WarningBrush"),
        });
        return new Border
        {
            Child = warning,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = Resource<Brush>("WarningBrush"),
            Background = new SolidColorBrush(Resource<Color>("WarningTintColor")),
        };
    }

    // Each item is its own rounded row with an accent edge, not a cell in a table.
    internal static ScrollViewer List(IEnumerable<ConfirmItem> items)
    {
        var rows = new StackPanel { Spacing = 6 };
        foreach (ConfirmItem item in items)
        {
            var row = new Grid
            {
                ColumnSpacing = 12,
                Padding = new Thickness(0, 0, 14, 0),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Resource<Color>("SurfaceColor")),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new Border { Background = new SolidColorBrush(item.Edge) });
            var name = new StackPanel { Padding = new Thickness(0, 10, 0, 10) };
            Grid.SetColumn(name, 1);
            name.Children.Add(new TextBlock { Text = item.Name, Foreground = Resource<Brush>("InkBrush") });
            name.Children.Add(new TextBlock
            {
                Text = item.Detail,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Resource<Brush>("InkSecondaryBrush"),
            });
            row.Children.Add(name);
            if (item.Size is not null)
            {
                var size = new TextBlock
                {
                    Text = item.Size,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = Resource<Brush>("InkBrush"),
                };
                Grid.SetColumn(size, 2);
                row.Children.Add(size);
            }

            rows.Children.Add(row);
        }

        return new ScrollViewer { Content = rows, MaxHeight = 240 };
    }

    internal static StackPanel Note(string text)
    {
        var note = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        note.Children.Add(new FontIcon { Glyph = NoteGlyph, FontSize = 13, Foreground = Resource<Brush>("InkSecondaryBrush") });
        note.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            Foreground = Resource<Brush>("InkSecondaryBrush"),
        });
        return note;
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];

    private void OnConfirmClick(object sender, RoutedEventArgs e) => _answer?.TrySetResult(true);

    private void OnCancelClick(object sender, RoutedEventArgs e) => _answer?.TrySetResult(false);

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            _answer?.TrySetResult(false);
            e.Handled = true;
        }
    }

    private void OnHoleUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args) =>
        _hole.Advance(args.Timing.ElapsedTime);

    // In a contrast theme the hole is not drawn (Aşama 3f): its colours are fixed VOID ones.
    private void OnHoleDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (!ContrastMode.IsOn)
        {
            _hole.Draw(args.DrawingSession, sender.Size.ToVector2());
        }
    }
}
