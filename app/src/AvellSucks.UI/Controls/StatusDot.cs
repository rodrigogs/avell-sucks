using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AvellSucks.UI.Controls;

/// <summary>
/// Compact decorative state indicator used beside an accessible toggle:
/// On = solid green circle, Off = muted ring, Unknown = amber bar.
/// Shape and color both change so the states remain distinguishable without
/// relying on color alone. The adjacent toggle carries the accessible name/state.
/// </summary>
public enum DeviceStatus { On, Off, Unknown }

public sealed class StatusDot : Grid
{
    private readonly Ellipse _onDot;
    private readonly Ellipse _offRing;
    private readonly Rectangle _unknownBar;

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(DeviceStatus), typeof(StatusDot),
        new PropertyMetadata(DeviceStatus.Unknown, OnStatusChanged));

    public DeviceStatus Status
    {
        get => (DeviceStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public StatusDot()
    {
        Width = 10;
        Height = 16;
        VerticalAlignment = VerticalAlignment.Center;
        Focusable = false;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;

        _onDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = (Brush)Application.Current.FindResource("Ok"),
        };
        _offRing = new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = Brushes.Transparent,
            Stroke = (Brush)Application.Current.FindResource("Ink3"),
            StrokeThickness = 1.5,
        };
        _unknownBar = new Rectangle
        {
            Width = 9,
            Height = 3,
            RadiusX = 1.5,
            RadiusY = 1.5,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = (Brush)Application.Current.FindResource("Warn"),
        };

        Children.Add(_onDot);
        Children.Add(_offRing);
        Children.Add(_unknownBar);
        ApplyStatus();
    }

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((StatusDot)d).ApplyStatus();

    private void ApplyStatus()
    {
        _onDot.Visibility = Status == DeviceStatus.On ? Visibility.Visible : Visibility.Collapsed;
        _offRing.Visibility = Status == DeviceStatus.Off ? Visibility.Visible : Visibility.Collapsed;
        _unknownBar.Visibility = Status == DeviceStatus.Unknown ? Visibility.Visible : Visibility.Collapsed;
    }
}
