using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class SafeHttpTlsCollector
{
    public byte[]? CertificateDer { get; set; }
}

internal static class SafeHttpTlsOptions
{
    public static readonly HttpRequestOptionsKey<SafeHttpTlsCollector> Key = new("WebHealth.Tls");
}

internal static class TlsCertificateReader
{
    private const int MaxNameLength = 512;
    private const int MaxSubjectAlternativeNames = 20;

    public static TlsCertificateObservation? TryRead(
        byte[]? encodedCertificate,
        bool hostnameMatched,
        bool chainTrusted,
        DateTimeOffset observedAt)
    {
        if (encodedCertificate is null or { Length: 0 })
        {
            return null;
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadCertificate(encodedCertificate);
        }
        catch (CryptographicException)
        {
            return null;
        }

        using (certificate)
        {
            var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);
            var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);

            return new TlsCertificateObservation(
                Truncate(certificate.Subject),
                Truncate(certificate.Issuer),
                Truncate(certificate.SerialNumber),
                Convert.ToHexStringLower(certificate.GetCertHash(HashAlgorithmName.SHA256)),
                notBefore,
                notAfter,
                ReadSubjectAlternativeNames(certificate),
                hostnameMatched,
                chainTrusted,
                TlsCertificateEvaluator.Classify(
                    notBefore, notAfter, observedAt, hostnameMatched, chainTrusted),
                observedAt);
        }
    }

    private static IReadOnlyList<string> ReadSubjectAlternativeNames(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .FirstOrDefault();
        if (extension is null)
        {
            return [];
        }

        try
        {
            return [.. extension.EnumerateDnsNames()
                .Take(MaxSubjectAlternativeNames)
                .Select(Truncate)];
        }
        catch (CryptographicException)
        {
            return [];
        }
    }

    private static string Truncate(string value) =>
        value.Length <= MaxNameLength ? value : value[..MaxNameLength];
}
