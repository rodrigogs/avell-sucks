using System.IO;
using AvellSucks.Core.Service;
using Xunit;

namespace AvellSucks.Core.Tests.Service;

public class SelfSignedCertFactoryTests
{
    [Fact]
    public void Create_produces_cert_with_private_key_and_subject()
    {
        using var cert = SelfSignedCertFactory.Create("avellsucks-local");
        Assert.True(cert.HasPrivateKey);
        Assert.Contains("avellsucks-local", cert.Subject);
    }

    [Fact]
    public void ExportPfx_writes_loadable_file()
    {
        using var cert = SelfSignedCertFactory.Create("avellsucks-local");
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pfx");
        try
        {
            SelfSignedCertFactory.ExportPfx(cert, path, "pw");
            Assert.True(File.Exists(path));
            using var loaded = System.Security.Cryptography.X509Certificates.X509CertificateLoader
                .LoadPkcs12FromFile(path, "pw");
            Assert.True(loaded.HasPrivateKey);
        }
        finally { File.Delete(path); }
    }
}
