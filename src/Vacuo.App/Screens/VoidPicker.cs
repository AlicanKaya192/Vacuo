using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Vacuo.App.Screens;

/// <summary>
/// A drop-down in the VOID look (vacuo-kurallar 15): a ghost button showing the choice and a turning
/// chevron, opening a dark rounded list whose chosen row has a lit edge. Replaces the stock ComboBox.
/// </summary>
public sealed class VoidPicker : UserControl
{
    private const string ChevronDown = "";

    private readonly Button _trigger;
    private readonly TextBlock _label;
    private readonly FontIcon _chevron;
    private readonly Flyout _flyout;
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly List<(string Text, object Value)> _items = [];
    private int _selectedIndex = -1;

    public VoidPicker()
    {
        _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _chevron = new FontIcon
        {
            Glyph = ChevronDown,
            FontSize = 12,
            Foreground = Resource<Brush>("InkSecondaryBrush"),
            RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new RotateTransform(),
        };
        var face = new Grid { ColumnSpacing = 10 };
        face.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        face.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        face.Children.Add(_label);
        Grid.SetColumn(_chevron, 1);
        face.Children.Add(_chevron);

        _flyout = new Flyout
        {
            Content = _list,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            FlyoutPresenterStyle = Resource<Style>("VoidFlyoutPresenterStyle"),
        };
        _flyout.Opening += (_, _) => OnOpening();
        _flyout.Closed += (_, _) => Turn(0);
        _trigger = new Button { Content = face, Style = Resource<Style>("VoidPickerTriggerStyle"), Flyout = _flyout };
        Content = _trigger;
    }

    /// <summary>Raised when the user picks another option; setting <see cref="SelectedIndex"/> in code does not raise it.</summary>
    public event EventHandler? SelectionChanged;

    public object? SelectedValue => _selectedIndex >= 0 ? _items[_selectedIndex].Value : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            _label.Text = value >= 0 ? _items[value].Text : string.Empty;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_trigger, _label.Text);
        }
    }

    public void SetItems(IEnumerable<(string Text, object Value)> items)
    {
        _items.Clear();
        _items.AddRange(items);
        SelectedIndex = _items.Count > 0 ? 0 : -1;
    }

    // The rows are built on each opening, so the chosen one is always the lit one.
    private void OnOpening()
    {
        _list.Children.Clear();
        _list.MinWidth = Math.Max(0, _trigger.ActualWidth - 14);
        for (int index = 0; index < _items.Count; index++)
        {
            int chosen = index;
            var row = new Button
            {
                Content = _items[index].Text,
                Style = Resource<Style>(index == _selectedIndex ? "VoidPickerSelectedItemStyle" : "VoidPickerItemStyle"),
            };
            row.Click += (_, _) => Choose(chosen);
            _list.Children.Add(row);
        }

        Turn(180);
    }

    private void Choose(int index)
    {
        _flyout.Hide();
        if (index == _selectedIndex)
        {
            return;
        }

        SelectedIndex = index;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Turn(double degrees) => ((RotateTransform)_chevron.RenderTransform).Angle = degrees;

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
