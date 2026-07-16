namespace AvellSucks.Server.Security;

/// <summary>
/// Pure thumbprint-match logic for mTLS client-cert validation, extracted so it
/// is unit-testable without a live TLS handshake (TestServer/WebApplicationFactory
/// bypass Kestrel TLS, so the end-to-end client-cert handshake is validated by the
/// Task 18 elevated smoke test instead — see plan). Used by
/// <c>OnCertificateValidated</c> in ServerHostBuilder.
/// </summary>
public static class CertificateThumbprint
{
    /// <summary>
    /// True when the presented cert thumbprint matches the configured one after
    /// normalization (strip ':' and whitespace, case-insensitive). Returns false
    /// when the configured thumbprint is null/empty (nothing to match against) or
    /// on any mismatch.
    /// </summary>
    public static bool Matches(string? presentedThumbprint, string? configuredThumbprint)
    {
        var expected = Normalize(configuredThumbprint);
        if (string.IsNullOrEmpty(expected)) return false;
        var actual = Normalize(presentedThumbprint);
        if (string.IsNullOrEmpty(actual)) return false;
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? thumbprint)
    {
        if (string.IsNullOrEmpty(thumbprint)) return string.Empty;
        Span<char> buffer = stackalloc char[thumbprint.Length];
        var len = 0;
        foreach (var c in thumbprint)
        {
            if (c == ':' || char.IsWhiteSpace(c)) continue;
            buffer[len++] = c;
        }
        return new string(buffer[..len]);
    }
}
