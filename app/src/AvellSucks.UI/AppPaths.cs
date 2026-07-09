using System;
using System.IO;

namespace AvellSucks.UI;

/// <summary>
/// Per-machine writable locations for logs and the EC-write audit trail.
///
/// The app installs per-machine into Program Files, which is read-only for the
/// process even when elevated (writing there is bad practice and breaks a clean
/// update). So diagnostics live under %ProgramData%\AvellSucks instead: writable,
/// shared per-machine, and untouched by an in-place installer update. Falls back
/// to the exe directory if ProgramData can't be resolved (e.g. an odd sandbox).
/// </summary>
public static class AppPaths
{
    /// <summary>%ProgramData%\AvellSucks (created on first use).</summary>
    public static string DataDir { get; } = ResolveDataDir();

    /// <summary>Startup/diagnostic trace log.</summary>
    public static string LogFile => Path.Combine(DataDir, "avellsucks.log");

    /// <summary>Directory for the EC-write audit JSONL.</summary>
    public static string AuditDir => Path.Combine(DataDir, "audit");

    private static string ResolveDataDir()
    {
        try
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var dir = string.IsNullOrEmpty(baseDir)
                ? AppContext.BaseDirectory
                : Path.Combine(baseDir, "AvellSucks");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }
}
