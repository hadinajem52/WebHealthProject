using System.Net;

namespace WebHealth.Domain.Monitoring;

public static class DestinationAddressPolicy
{
    private static readonly NetworkRange[] ProhibitedIpv4Ranges =
    [
        Range("0.0.0.0", 8),
        Range("10.0.0.0", 8),
        Range("100.64.0.0", 10),
        Range("127.0.0.0", 8),
        Range("169.254.0.0", 16),
        Range("172.16.0.0", 12),
        Range("192.0.0.0", 24),
        Range("192.0.2.0", 24),
        Range("192.31.196.0", 24),
        Range("192.52.193.0", 24),
        Range("192.88.99.0", 24),
        Range("192.168.0.0", 16),
        Range("192.175.48.0", 24),
        Range("198.18.0.0", 15),
        Range("198.51.100.0", 24),
        Range("203.0.113.0", 24),
        Range("224.0.0.0", 4),
        Range("240.0.0.0", 4)
    ];

    private static readonly NetworkRange[] ProhibitedIpv6Ranges =
    [
        Range("::", 128),
        Range("::1", 128),
        Range("::", 96),
        Range("::ffff:0:0:0", 96),
        Range("64:ff9b::", 96),
        Range("64:ff9b:1::", 48),
        Range("100::", 64),
        Range("2001::", 23),
        Range("2001:db8::", 32),
        Range("2002::", 16),
        Range("3fff::", 20),
        Range("5f00::", 16),
        Range("2620:4f:8000::", 48),
        Range("fc00::", 7),
        Range("fe80::", 10),
        Range("fec0::", 10),
        Range("ff00::", 8)
    ];

    public static bool IsAllowed(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        var ranges = normalized.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? ProhibitedIpv4Ranges
            : ProhibitedIpv6Ranges;
        return ranges.All(range => !range.Contains(normalized));
    }

    private static NetworkRange Range(string address, int prefixLength) =>
        new(IPAddress.Parse(address).GetAddressBytes(), prefixLength);

    private sealed record NetworkRange(byte[] Network, int PrefixLength)
    {
        public bool Contains(IPAddress address)
        {
            var candidate = address.GetAddressBytes();
            if (candidate.Length != Network.Length)
            {
                return false;
            }

            var wholeBytes = PrefixLength / 8;
            if (!candidate.AsSpan(0, wholeBytes).SequenceEqual(Network.AsSpan(0, wholeBytes)))
            {
                return false;
            }

            var remainingBits = PrefixLength % 8;
            if (remainingBits == 0)
            {
                return true;
            }

            var mask = (byte)(0xff << (8 - remainingBits));
            return (candidate[wholeBytes] & mask) == (Network[wholeBytes] & mask);
        }
    }
}
