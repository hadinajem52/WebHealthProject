using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace WebHealth.Infrastructure.Monitoring;

/// <summary>
/// Decides whether the certificate chain is trusted, separately from whether the leaf is
/// currently within its validity window.
/// </summary>
internal static class TlsChainTrust
{
    private const X509ChainStatusFlags TimeValidity =
        X509ChainStatusFlags.NotTimeValid | X509ChainStatusFlags.CtlNotTimeValid;

    /// <summary>
    /// Time-validity failures are forgiven <em>on the leaf only</em>, because the leaf's own
    /// expiry is reported as its own category and would otherwise also be labelled untrusted,
    /// hiding the actionable cause. Every other element must be completely error-free: an
    /// expired intermediate or root breaks the chain for real clients and must never be
    /// reported as a valid certificate.
    /// </summary>
    /// <param name="elementStatuses">
    /// Per-element status flags, leaf first — the order <see cref="X509Chain.ChainElements" />
    /// uses. Aggregate <see cref="X509Chain.ChainStatus" /> cannot be used here because it does
    /// not say which element a failure came from.
    /// </param>
    public static bool Evaluate(SslPolicyErrors errors, IReadOnlyList<X509ChainStatusFlags> elementStatuses)
    {
        if (!errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
        {
            return true;
        }

        if (elementStatuses.Count is 0)
        {
            return false;
        }

        if ((elementStatuses[0] & ~TimeValidity) != X509ChainStatusFlags.NoError)
        {
            return false;
        }

        for (var element = 1; element < elementStatuses.Count; element++)
        {
            if (elementStatuses[element] != X509ChainStatusFlags.NoError)
            {
                return false;
            }
        }

        return true;
    }

    public static IReadOnlyList<X509ChainStatusFlags> ReadElementStatuses(X509Chain? chain)
    {
        if (chain is null)
        {
            return [];
        }

        var statuses = new X509ChainStatusFlags[chain.ChainElements.Count];
        for (var element = 0; element < chain.ChainElements.Count; element++)
        {
            foreach (var status in chain.ChainElements[element].ChainElementStatus)
            {
                statuses[element] |= status.Status;
            }
        }

        return statuses;
    }
}
