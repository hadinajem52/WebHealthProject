using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class SafeDestinationException : HttpRequestException;

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
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? authorize = null)
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

        var connectStart = Stopwatch.GetTimestamp();
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(options.PerAddressConnectTimeout);
            IDisposable? addressLease = null;
            Socket? socket = null;
            try
            {
                addressLease = await limiter.AcquireAddressAsync(address.ToString(), attempt.Token);
                if (authorize is not null && !await authorize(attempt.Token))
                {
                    throw new SafeDestinationException();
                }
                socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(new IPEndPoint(address, port), attempt.Token);
                cancellationToken.ThrowIfCancellationRequested();
                if (socket.RemoteEndPoint is not IPEndPoint peer
                    || !Normalize(peer.Address).Equals(address)
                    || peer.Port != port
                    || !addressPolicy.IsAllowed(Normalize(peer.Address)))
                {
                    throw new SafeDestinationException();
                }

                if (timing is not null)
                {
                    var completedAt = Stopwatch.GetTimestamp();
                    timing.ConnectDurationMs = SafeHttpTimingMath.ElapsedMs(connectStart, completedAt);
                    timing.ConnectCompletedTimestamp = completedAt;
                }

                var stream = new LeaseReleasingStream(new NetworkStream(socket, ownsSocket: true), addressLease);
                socket = null;
                addressLease = null;
                return stream;
            }
            catch (SocketException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt.IsCancellationRequested)
            {
            }
            finally
            {
                socket?.Dispose();
                addressLease?.Dispose();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new SocketException((int)SocketError.NotConnected);
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
