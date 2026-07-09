using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AvellSucks.UI.Controls;

/// <summary>Severity of a <see cref="NoticeBanner"/> — picks its tint + accent.</summary>
public enum NoticeSeverity { Warn, Danger }

/// <summary>
/// A tinted inline notice: an MDL2 glyph pinned left + a wrapping message, on a
/// severity-tinted panel. Replaces the Border+DockPanel+glyph+wrapping-text block
/// that was copy-pasted across the Fan / Performance / RGB / Dashboard tabs (write
/// gate, advanced warning, device-missing, degraded-sensor notices), which drifted
/// in padding and corner radius. Composed in code like <see cref="DiskUsageRow"/>;
/// call sites just set Severity/Glyph/Text and toggle Visibility.
/// </summary>
public sealed class NoticeBanner : Border
{
    private readonly TextBlock _glyph;
    private readonly TextBlock _message;

    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(NoticeSeverity), typeof(NoticeBanner),
        new PropertyMetadata(NoticeSeverity.Warn, OnSeverityChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(NoticeBanner), new PropertyMetadata(""));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(NoticeBanner), new PropertyMetadata(""));

    /// <summary>Compact caption sizing (for the dense Advanced power-limit warning).</summary>
    public static readonly DependencyProperty SmallProperty = DependencyProperty.Register(
        nameof(Small), typeof(bool), typeof(NoticeBanner),
        new PropertyMetadata(false, OnSmallChanged));

    public NoticeSeverity Severity { get => (NoticeSeverity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool Small { get => (bool)GetValue(SmallProperty); set => SetValue(SmallProperty, value); }

    public NoticeBanner()
    {
        // Panel geometry mirrors PanelTight (padding 13, RadiusMd) so every notice
        // reads as the same surface — the drift the copies had is gone.
        BorderThickness = new Thickness(1);
        CornerRadius = (CornerRadius)Application.Current.FindResource("RadiusMd");
        Padding = new Thickness(13);
        SnapsToDevicePixels = true;

        // DockPanel (not a horizontal StackPanel): the message fills the bounded
        // remaining width so TextWrapping actually engages.
        var dock = new DockPanel();

        _glyph = new TextBlock
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0),
        };
        DockPanel.SetDock(_glyph, Dock.Left);
        BindingOperations.SetBinding(_glyph, TextBlock.TextProperty, new Binding(nameof(Glyph)) { Source = this });
        dock.Children.Add(_glyph);

        _message = new TextBlock
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.FindResource("Ink"),
            TextWrapping = TextWrapping.Wrap,
        };
        BindingOperations.SetBinding(_message, TextBlock.TextProperty, new Binding(nameof(Text)) { Source = this });
        dock.Children.Add(_message);

        Child = dock;
        ApplySeverity();
        ApplySmall();
    }

    private static void OnSeverityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((NoticeBanner)d).ApplySeverity();

    private static void OnSmallChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((NoticeBanner)d).ApplySmall();

    private void ApplySeverity()
    {
        var (tint, accent) = Severity == NoticeSeverity.Danger
            ? ("DangerTint", "Danger")
            : ("WarnTint", "Warn");
        Background = (Brush)Application.Current.FindResource(tint);
        var accentBrush = (Brush)Application.Current.FindResource(accent);
        BorderBrush = accentBrush;
        _glyph.Foreground = accentBrush;
    }

    private void ApplySmall()
        => _message.FontSize = Small
            ? (double)Application.Current.FindResource("FsCaption")
            : (double)Application.Current.FindResource("FsBody");
}
