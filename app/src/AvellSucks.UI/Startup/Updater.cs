using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AvellSucks.UI.Startup;

/// <summary>Outcome of an update check.</summary>
public enum UpdateStatus
{
    UpToDate,
    UpdateAvailable,
    NoConnection,
    Error,
}

/// <summary>Result of a check: status plus the release version/asset when one is newer.</summary>
public sealed record UpdateCheck(UpdateStatus Status, string? LatestVersion = null, string? AssetUrl = null, string? Error = null);

/// <summary>
/// In-app updater. Checks the GitHub Releases API for a newer tagged release,
/// and (when asked) downloads the Inno Setup installer asset and relaunches it
/// silently so it upgrades the per-machine install in place.
///
/// Because the app runs elevated (requireAdministrator), the child installer
/// inherits elevation and can write Program Files with no extra UAC prompt. The
/// running app must exit right after spawning the installer — a self-contained
/// app holds its own DLLs locked while alive — so ApplyUpdate relaunches via a
/// cmd chain that runs the installer, then starts the app back up.
/// </summary>
public static class Updater
{
    private const string Owner = "rodrigogs";
    private const string Repo = "avell-sucks";
    private const string AssetName = "AvellSucks-Setup.exe"; // must match the .iss OutputBaseFilename
    private const string LatestReleaseUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private const string ReleasesPageUrl = $"https://github.com/{Owner}/{Repo}/releases";

    // GitHub REST requires a User-Agent; without one it returns 403.
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AvellSucks-Updater", CurrentVersion().ToString()));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>The running app's version (from InformationalVersion, CI-injected from the git tag).</summary>
    public static Version CurrentVersion()
    {
        try
        {
            var info = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            // Trim any SemVer build/prerelease suffix ("1.2.3+abc", "1.2.3-rc1").
            var core = (info ?? "0.0.0").Split('+', '-')[0];
            return NormalizeVersion(core);
        }
        catch { return new Version(0, 0, 0, 0); }
    }

    /// <summary>Query GitHub for the latest release and compare to the running version.</summary>
    public static async Task<UpdateCheck> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync(LatestReleaseUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new UpdateCheck(UpdateStatus.Error, Error: $"HTTP {(int)resp.StatusCode}");

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
                return new UpdateCheck(UpdateStatus.Error, Error: "no tag_name");

            var latest = NormalizeVersion(tag.TrimStart('v', 'V'));
            if (latest <= CurrentVersion())
                return new UpdateCheck(UpdateStatus.UpToDate, latest.ToString());

            // Find the installer asset by exact name (never assets[0]).
            string? assetUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase)
                        && a.TryGetProperty("browser_download_url", out var u))
                    {
                        assetUrl = u.GetString();
                        break;
                    }
                }
            }

            return new UpdateCheck(UpdateStatus.UpdateAvailable, latest.ToString(), assetUrl);
        }
        catch (OperationCanceledException) { return new UpdateCheck(UpdateStatus.NoConnection, Error: "timeout"); }
        catch (HttpRequestException ex) { return new UpdateCheck(UpdateStatus.NoConnection, Error: ex.Message); }
        catch (Exception ex) { return new UpdateCheck(UpdateStatus.Error, Error: ex.Message); }
    }

    /// <summary>
    /// Download the installer for <paramref name="check"/> and relaunch it silently,
    /// then shut the app down so the installer can overwrite the files. Returns
    /// false (app keeps running) if the download failed; on success it does not
    /// return in a normal sense — the app is exiting.
    /// </summary>
    public static async Task<bool> DownloadAndApplyAsync(UpdateCheck check, Action shutdown, CancellationToken ct = default)
    {
        if (check.Status != UpdateStatus.UpdateAvailable || string.IsNullOrWhiteSpace(check.AssetUrl))
            return false;

        try
        {
            // Version-stamped temp path; clear any stale partial from a prior try.
            var setupPath = Path.Combine(Path.GetTempPath(), $"AvellSucks-Setup-{check.LatestVersion}.exe");
            if (File.Exists(setupPath)) File.Delete(setupPath);

            using (var resp = await Http.GetAsync(check.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = File.Create(setupPath);
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            }

            // Sanity check: a real installer is well over 1 MB. A tiny file means an
            // error page / redirect slipped through — don't run it.
            if (new FileInfo(setupPath).Length < 1_000_000)
            {
                App.Trace($"Updater: downloaded asset too small ({new FileInfo(setupPath).Length} bytes) — aborting.");
                return false;
            }

            var appExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            var logPath = Path.Combine(Path.GetTempPath(), "AvellSucks-update.log");

            // cmd chain: run the installer silently, then relaunch the app. No
            // /SUPPRESSMSGBOXES (it can silently cancel the close-apps step). The
            // child tree is elevated (inherited), so the Program Files write is fine.
            var args = $"/c \"\"{setupPath}\" /VERYSILENT /NORESTART /LOG=\"{logPath}\"" +
                       (appExe is not null ? $" & start \"\" \"{appExe}\"" : "") + "\"";

            Process.Start(new ProcessStartInfo("cmd.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            // Give the installer's Restart Manager a moment to register our PID
            // before we exit (exiting too fast can defeat the close/relaunch logic).
            await Task.Delay(500, ct).ConfigureAwait(false);
            App.Trace($"Updater: launched silent install of {check.LatestVersion}; shutting down.");
            shutdown();
            return true;
        }
        catch (Exception ex)
        {
            App.Trace($"Updater: DownloadAndApply failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Open the releases page in the browser (fallback when there's no installer asset).</summary>
    public static void OpenReleasesPage()
    {
        try { Process.Start(new ProcessStartInfo(ReleasesPageUrl) { UseShellExecute = true }); }
        catch { /* no browser */ }
    }

    // Pad to a 4-component Version so "1.7" and "1.7.0.0" compare equal.
    private static Version NormalizeVersion(string s)
    {
        var parts = s.Trim().Split('.');
        int P(int i) => i < parts.Length && int.TryParse(parts[i], out var v) ? v : 0;
        return new Version(P(0), P(1), P(2), P(3));
    }
}
