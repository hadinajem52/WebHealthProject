using System.Net;

namespace WebHealth.Domain.Monitoring;

public static class DestinationHostPolicy
{
    private static readonly string[] ReservedSuffixes =
        [".localhost", ".local", ".internal", ".home.arpa"];

    private static readonly string[] ReservedNames = ["localhost"];

    public static bool IsDefinitelyUnreachable(string? host, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var candidate = host.Trim().TrimEnd('.').Trim('[', ']');
        if (IPAddress.TryParse(candidate, out var address))
        {
            if (DestinationAddressPolicy.IsAllowed(address))
            {
                return false;
            }

            reason = $"{candidate} is a private, loopback or otherwise reserved address. This "
                + "application only monitors targets reachable over the public internet, so no "
                + "check against it could ever run. Use a publicly resolvable address.";
            return true;
        }

        var lowered = candidate.ToLowerInvariant();
        if (ReservedNames.Contains(lowered, StringComparer.Ordinal)
            || ReservedSuffixes.Any(suffix => lowered.EndsWith(suffix, StringComparison.Ordinal)))
        {
            reason = $"{candidate} is a reserved local name that only resolves inside a private "
                + "network. This application only monitors targets reachable over the public "
                + "internet, so no check against it could ever run. Use a public host name.";
            return true;
        }

        return false;
    }
}
