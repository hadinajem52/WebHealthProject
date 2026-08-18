using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class SafeDestinationException : HttpRequestException;

/// <summary>
/// The single implementation of "open a TCP connection to a monitored host safely": resolve,
/// apply destination policy to every answer, connect, then verify the address actually
/// connected to (BR-Q01, BR-Q02). Both the monitoring HTTP handler and the SSL certificate
/// probe go through here, so neither can drift away from the enforced network policy.
/// </summary>
internal static class SafeDestinationConnector
{
    public static async Task<Stream> ConnectAsync(
        IMonitoringDnsResolver resolver,
        IDestinationAddressPolicy addressPolicy,
        SafeHttpConcurrencyLimiter limiter,
        SafeHttpTransportOptions options,
        string host,
        int port,
        SafeHttpTimingCollector? timing,
        CancellationToken cancellationToken)
    {
        var dnsStart = Stopwatch.GetTimestamp();
        var answers = await resolver.ResolveAsync(host, cancellationToken);
        if (timing is not null)
        {
            timing.DnsDurationMs = SafeHttpTimingMath.ElapsedMs(dnsStart);
        }

        if (answers.Count is 0 || answers.Count > options.MaxDnsAnswers)
        {
            throw new SafeDestinationException();
        }

        var addresses = answers
            .Select(Normalize)
            .Distinct()
            .ToArray();
        if (addresses.Any(address => !addressPolicy.IsAllowed(address)))
        {
            throw new SafeDestinationException();
        }

        var selected = addresses[0];
        var addressLease = await limiter.AcquireAddressAsync(selected.ToString(), cancellationToken);
        var socket = new Socket(selected.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            var connectStart = Stopwatch.GetTimestamp();
            await socket.ConnectAsync(new IPEndPoint(selected, port), cancellationToken);
            if (socket.RemoteEndPoint is not IPEndPoint peer
                || !Normalize(peer.Address).Equals(selected)
                || peer.Port != port
                || !addressPolicy.IsAllowed(peer.Address))
            {
                throw new SafeDestinationException();
            }

            if (timing is not null)
            {
                var connectCompletedAt = Stopwatch.GetTimestamp();
                timing.ConnectDurationMs = SafeHttpTimingMath.ElapsedMs(connectStart, connectCompletedAt);
                timing.ConnectCompletedTimestamp = connectCompletedAt;
            }

            return new LeaseReleasingStream(new NetworkStream(socket, ownsSocket: true), addressLease);
        }
        catch
        {
            socket.Dispose();
            addressLease.Dispose();
            throw;
        }
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private sealed class LeaseReleasingStream(Stream inner, IDisposable lease) : Stream
    {
        private IDisposable? _lease = lease;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                Interlocked.Exchange(ref _lease, null)?.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            Interlocked.Exchange(ref _lease, null)?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
