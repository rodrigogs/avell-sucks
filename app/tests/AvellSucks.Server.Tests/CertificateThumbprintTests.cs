using AvellSucks.Server.Security;
using Xunit;

namespace AvellSucks.Server.Tests;

public class CertificateThumbprintTests
{
    [Fact]
    public void Exact_match_returns_true()
        => Assert.True(CertificateThumbprint.Matches(
            "ABCDEF0123456789", "ABCDEF0123456789"));

    [Theory]
    [InlineData("AB:CD:EF:01", "ABCDEF01")]        // colon-separated presented
    [InlineData("ABCDEF01", "AB:CD:EF:01")]        // colon-separated configured
    [InlineData("ab cd ef 01", "ABCDEF01")]        // spaces + case-insensitive
    [InlineData("  abcdef01  ", "ABCDEF01")]       // surrounding whitespace + case
    [InlineData("AB:CD:EF:01", "ab:cd:ef:01")]     // both colon-separated, mixed case
    public void Normalized_match_returns_true(string presented, string configured)
        => Assert.True(CertificateThumbprint.Matches(presented, configured));

    [Fact]
    public void Mismatch_returns_false()
        => Assert.False(CertificateThumbprint.Matches("ABCDEF01", "DEADBEEF"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_empty_configured_returns_false(string? configured)
        => Assert.False(CertificateThumbprint.Matches("ABCDEF01", configured));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_or_empty_presented_returns_false(string? presented)
        => Assert.False(CertificateThumbprint.Matches(presented, "ABCDEF01"));

    // FAIL-CLOSED guard: when mTLS is enabled but no allowed thumbprint is configured,
    // even a real presented cert must NOT match. This underpins ServerHostBuilder's
    // OnCertificateValidated rejecting every client cert instead of failing open.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_configured_never_matches_a_real_presented_cert(string? configured)
        => Assert.False(CertificateThumbprint.Matches("ABCDEF0123456789", configured));
}
