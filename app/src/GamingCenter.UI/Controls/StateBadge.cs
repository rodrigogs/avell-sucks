using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamingCenter.UI.Services;

namespace GamingCenter.UI.Controls;

/// <summary>
/// Pill that reports a write result with icon + label + color — never color
/// alone. Reflects the allowed/executed/verified/blocked contract.
/// Built in code so it needs no Generic.xaml.
/// </summary>
public sealed class StateBadge : Border
{
    private readonly TextBlock _icon;
    private readonly TextBlock _label;

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(WriteState), typeof(StateBadge),
        new FrameworkPropertyMetadata(WriteState.Idle, (d, _) => ((StateBadge)d).Apply()));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(StateBadge),
        new FrameworkPropertyMetadata("", (d, _) => ((StateBadge)d).Apply()));

    public WriteState State { get => (WriteState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }

    public StateBadge()
    {
        CornerRadius = new CornerRadius(999);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(11, 6, 13, 6);
        HorizontalAlignment = HorizontalAlignment.Left;
        SnapsToDevicePixels = true;

        _icon = new TextBlock
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        _label = new TextBlock
        {
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(_icon);
        stack.Children.Add(_label);
        Child = stack;
        Apply();
    }

    private void Apply()
    {
        // Segoe MDL2 Assets glyphs by codepoint.
        (string glyph, string text, Color color) = State switch
        {
            WriteState.Verified => ("\uE930", "Applied & verified", Rgb(0x34, 0xE5, 0xA0)), // Completed
            WriteState.Pending  => ("\uE895", "Applying\u2026",     Rgb(0x22, 0xD3, 0xEE)), // Sync
            WriteState.Failed   => ("\uEA39", "Write failed",        Rgb(0xF5, 0x48, 0x4A)), // ErrorBadge
            WriteState.Blocked  => ("\uE733", "Blocked",             Rgb(0xF4, 0xC0, 0x4A)), // Blocked
            _                   => ("\uEA3A", "Ready",               Rgb(0x94, 0x8A, 0xA3)), // StatusCircleRing
        };

        _icon.Text = glyph;
        _icon.Foreground = new SolidColorBrush(color);
        _label.Text = string.IsNullOrWhiteSpace(Message) ? text : $"{text} \u2014 {Message}";
        _label.Foreground = new SolidColorBrush(Rgb(0xF2, 0xEE, 0xF6));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, color.R, color.G, color.B));
        Background = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B));
        Visibility = State == WriteState.Idle && string.IsNullOrWhiteSpace(Message)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
