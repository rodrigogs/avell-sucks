namespace AvellSucks.UI;

/// <summary>
/// Single source of truth for the project's GitHub identity, shared by the
/// updater and the About page so the owner/repo can't drift between them.
/// (The About page also shows the URL as literal display text in XAML.)
/// </summary>
public static class AppInfo
{
    public const string Owner = "rodrigogs";
    public const string Repo = "avell-sucks";

    /// <summary>Installer asset name; must match the .iss OutputBaseFilename.</summary>
    public const string SetupAssetName = "AvellSucks-Setup.exe";

    public const string RepoUrl = $"https://github.com/{Owner}/{Repo}";
    public const string ReleasesPageUrl = $"{RepoUrl}/releases";
    public const string LatestReleaseApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
}
