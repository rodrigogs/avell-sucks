using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;

namespace AvellSucks.UI.Views;

public partial class AboutView : UserControl
{
    private const string RepoUrl = AppInfo.RepoUrl;

    public AboutView() => InitializeComponent();

    // Open the repository in the default browser.
    private void OnRepoClick(object sender, MouseButtonEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true }); }
        catch { /* no browser / sandbox — the URL is shown on screen anyway */ }
    }
}
