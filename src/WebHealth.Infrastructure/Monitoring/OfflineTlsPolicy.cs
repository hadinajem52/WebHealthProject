using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace WebHealth.Infrastructure.Monitoring;

internal static class OfflineTlsPolicy
{
    public static SslClientAuthenticationOptions Create(string targetHost = "") => new()
    {
        TargetHost = targetHost,
        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        CertificateChainPolicy = new X509ChainPolicy
        {
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
            VerificationFlags = X509VerificationFlags.NoFlag
        }
    };
}
