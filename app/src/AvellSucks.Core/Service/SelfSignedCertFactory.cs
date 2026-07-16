using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AvellSucks.Core.Service;

/// <summary>
/// Creates a self-signed RSA certificate for the local HTTPS listener. This is a
/// convenience for turning on HTTPS on a home/Tailscale network — clients must
/// trust it explicitly (documented). Not a public-CA substitute.
/// </summary>
public static class SelfSignedCertFactory
{
    public static X509Certificate2 Create(string subjectCn)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={subjectCn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], false)); // server auth
        var now = DateTimeOffset.UtcNow;
        return req.CreateSelfSigned(now.AddDays(-1), now.AddYears(5));
    }

    public static string ExportPfx(X509Certificate2 cert, string path, string? password)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        return path;
    }
}
