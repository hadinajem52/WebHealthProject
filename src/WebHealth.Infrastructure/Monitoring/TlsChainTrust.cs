using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace WebHealth.Infrastructure.Monitoring;

internal static class TlsChainTrust
{
    private const X509ChainStatusFlags TimeValidity =
        X509ChainStatusFlags.NotTimeValid | X509ChainStatusFlags.CtlNotTimeValid;

    public static bool Evaluate(
        SslPolicyErrors errors,
        IReadOnlyList<X509ChainStatusFlags> elementStatuses,
        X509ChainStatusFlags chainStatus = X509ChainStatusFlags.NoError)
    {
        if ((chainStatus & ~TimeValidity) != X509ChainStatusFlags.NoError)
        {
            return false;
        }

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

    public static IReadOnlyList<string> CanonicalStatusCodes(IEnumerable<X509ChainStatusFlags> statuses)
    {
        var combined = statuses.Aggregate(X509ChainStatusFlags.NoError, (all, status) => all | status);
        return Enum.GetValues<X509ChainStatusFlags>()
            .Where(flag => flag != X509ChainStatusFlags.NoError && (combined & flag) == flag)
            .Select(flag => flag.ToString())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(32)
            .ToArray();
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
