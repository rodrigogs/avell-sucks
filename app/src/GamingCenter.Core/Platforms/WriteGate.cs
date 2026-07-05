namespace GamingCenter.Core.Platforms;

/// <summary>
/// Explicit gate controlling whether EC writes may proceed.
/// Disabled by default; must be deliberately constructed or enabled via
/// the <c>GAMINGCENTER_ALLOW_EC_WRITES</c> environment variable.
/// </summary>
public sealed class WriteGate
{
    /// <summary>
    /// Shared default instance — always denies writes.
    /// </summary>
    public static WriteGate Disabled { get; } = new(allowWrites: false);

    private readonly bool _allowWrites;

    public WriteGate(bool allowWrites) => _allowWrites = allowWrites;

    /// <summary>
    /// True only when writes have been explicitly enabled.
    /// </summary>
    public bool IsWriteAllowed => _allowWrites;

    /// <summary>
    /// Creates a gate whose state is driven by the
    /// <c>GAMINGCENTER_ALLOW_EC_WRITES</c> environment variable.
    /// Recognised truthy values: <c>1</c>, <c>true</c> (case-insensitive).
    /// Everything else — including unset — yields a disabled gate.
    /// </summary>
    public static WriteGate FromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("GAMINGCENTER_ALLOW_EC_WRITES");
        var allow = raw is "1" or "true" or "TRUE" or "True";
        return new WriteGate(allow);
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if writes are not enabled.
    /// Call sites use this as a guard clause before any write path.
    /// </summary>
    public void EnsureAllowed()
    {
        if (!_allowWrites)
            throw new InvalidOperationException(
                "EC writes are disabled by default. "
                + "Construct WriteGate(allowWrites: true) or set "
                + "GAMINGCENTER_ALLOW_EC_WRITES=1 to enable.");
    }
}
