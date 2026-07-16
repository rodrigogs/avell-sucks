namespace AvellSucks.Core.Service;

/// <summary>
/// Canonical on-disk location of the shared service config. Lives under
/// CommonApplicationData (%ProgramData% on Windows) so the elevated UI can write
/// it and the service (any account) can read it.
///
/// The env var <c>GAMINGCENTER_CONFIG_PATH</c> overrides <see cref="ConfigFile"/>
/// (and, in turn, <see cref="Dir"/>) so tests can point the host at a per-test
/// temp file instead of the machine-global production config. Exposed as
/// getters (not static readonly fields) so the override is honored at access
/// time — a test can set the env var before the host builds.
/// </summary>
public static class ServiceConfigPaths
{
    /// <summary>Default directory when no override is set: %ProgramData%\AvellSucks.</summary>
    private static string DefaultDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AvellSucks");

    public static string ConfigFile =>
        Environment.GetEnvironmentVariable("GAMINGCENTER_CONFIG_PATH") is { Length: > 0 } p
            ? p
            : Path.Combine(DefaultDir, "service.json");

    public static string Dir => Path.GetDirectoryName(ConfigFile)!;
}
