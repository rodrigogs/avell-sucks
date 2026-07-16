using System.Security.Cryptography.X509Certificates;
using AvellSucks.Server.Hosting;
using Microsoft.AspNetCore.Authentication.Certificate;
using Xunit;

namespace AvellSucks.Server.Tests;

/// <summary>
/// Guards the mTLS chain-trust wiring so a PINNED self-signed client cert is not
/// rejected as "untrusted root" before the thumbprint pin runs. The full TLS
/// handshake is exercised by the elevated smoke test (scripts/mtls-positive.ps1);
/// here we assert the pure options seam that the host builder applies.
/// </summary>
public class ChainTrustModeTests
{
    [Fact]
    public void Uses_custom_root_trust_with_empty_store_so_system_chain_is_not_required()
    {
        var options = new CertificateAuthenticationOptions();

        ServerHostBuilder.ApplyThumbprintPinChainTrust(options);

        // Empty custom root store => the handler does NOT validate against the
        // Windows system store, so a self-signed client cert is not pre-rejected.
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, options.ChainTrustValidationMode);
        Assert.NotNull(options.CustomTrustStore);
        Assert.Empty(options.CustomTrustStore!);
    }

    [Fact]
    public void No_revocation_check_and_accepts_self_signed_but_still_checks_expiry()
    {
        var options = new CertificateAuthenticationOptions();

        ServerHostBuilder.ApplyThumbprintPinChainTrust(options);

        Assert.Equal(X509RevocationMode.NoCheck, options.RevocationMode);
        // Accept self-signed certs (product UI generates one); do not reject on EKU.
        Assert.Equal(CertificateTypes.All, options.AllowedCertificateTypes);
        Assert.False(options.ValidateCertificateUse);
        // Still reject an expired cert.
        Assert.True(options.ValidateValidityPeriod);
    }
}
