using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvellSucks.UI.Hardware;

namespace AvellSucks.UI.Controls;

/// <summary>
/// One disk drive's row on the dashboard: a color chip (drive identity) + letter,
/// a used/total readout, and a reused <see cref="CapacityBar"/> whose severity
/// fill recolors amber/red as the drive fills — so the color tags the drive while
/// the bar still reads as risk when full. Owns its own layout; the dashboard just
/// creates one per drive and calls <see cref="Update"/>.
/// </summary>
public sealed class DiskUsageRow : StackPanel
{
    private readonly CapacityBar _bar;
    private readonly TextBlock _text;

    public DiskUsageRow(string name, Color identityColor)
    {
        Margin = new Thickness(0, 0, 0, 6);

        var head = new DockPanel();
        var chip = new Border
        {
            CornerRadius = new CornerRadius(3),
            Background = Brand.Frozen(identityColor),
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = name.Replace(":", ""),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = Brand.Frozen(Brand.Bg),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        DockPanel.SetDock(chip, Dock.Left);
        head.Children.Add(chip);

        _text = new TextBlock
        {
            Text = "—",
            FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
            FontSize = 11,
            Foreground = Brand.Frozen(Brand.Ink3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        head.Children.Add(_text);

        _bar = new CapacityBar { Height = 7, Margin = new Thickness(0, 5, 0, 0), WarnAt = 0.85, CriticalAt = 0.95 };

        Children.Add(head);
        Children.Add(_bar);
    }

    public void Update(DriveUsage d)
    {
        _bar.Fraction = d.UsedFraction;
        // used / total; the tile header already carries total free, so the row
        // complements it instead of repeating "free".
        _text.Text = $"{FormatBytes(d.UsedBytes)} / {FormatBytes(d.TotalBytes)}";
    }

    /// <summary>GB up to 1 TB, then TB — matches the dashboard's disk headline.</summary>
    public static string FormatBytes(long b)
    {
        const double G = 1024d * 1024 * 1024;
        double gb = b / G;
        return gb >= 1024 ? $"{gb / 1024:0.0} TB" : $"{gb:0} GB";
    }
}
