namespace WebHealth.Domain.Monitoring;

/// <summary>
/// The validation state of an observed leaf certificate (BR-C03). A handshake that never
/// produced a certificate has no category at all: it is reported as a probe failure instead,
/// because there is nothing to describe.
/// </summary>
public enum TlsValidationCategory
{
    Valid,
    NotYetValid,
    Expired,
    HostnameMismatch,
    Untrusted
}

/// <summary>
/// Classifies an observed certificate into a single reportable category. All underlying
/// signals are still recorded alongside the category, so the precedence below only decides
/// which label is shown first, never which evidence is kept.
/// </summary>
public static class TlsCertificateEvaluator
{
    /// <summary>
    /// Precedence is time validity, then hostname, then trust. Time validity wins because it
    /// is the condition this system monitors and renews against, and because an expired
    /// certificate usually also reports chain errors — reporting it as merely "untrusted"
    /// would hide the actionable cause.
    /// </summary>
    /// <remarks>
    /// The validity window is inclusive at both ends, matching RFC 5280: a certificate is
    /// still valid at exactly <paramref name="notAfter" /> and already valid at exactly
    /// <paramref name="notBefore" />.
    /// </remarks>
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
