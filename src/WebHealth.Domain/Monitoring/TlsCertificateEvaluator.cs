namespace WebHealth.Domain.Monitoring;

public enum TlsValidationCategory
{
    Valid,
    NotYetValid,
    Expired,
    HostnameMismatch,
    Untrusted
}

public static class TlsCertificateEvaluator
{
    public static TlsValidationCategory Classify(
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        DateTimeOffset evaluatedAt,
        bool hostnameMatched,
        bool chainTrusted)
    {
        if (evaluatedAt < notBefore)
        {
            return TlsValidationCategory.NotYetValid;
        }

        if (evaluatedAt > notAfter)
        {
            return TlsValidationCategory.Expired;
        }

        if (!hostnameMatched)
        {
            return TlsValidationCategory.HostnameMismatch;
        }

        return chainTrusted ? TlsValidationCategory.Valid : TlsValidationCategory.Untrusted;
    }
}
