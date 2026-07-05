using System.Windows;

namespace GamingCenter.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnTestClick(object sender, RoutedEventArgs e) => StatusLine.Text = "Button works @ " + DateTime.Now.ToString("HH:mm:ss");
}
