using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace FeasibilitySpikes;

public sealed class SafeHttpSpikeTests
{
    [Fact]
    public async Task ConnectionCallbackPinsPermittedIpv4AndIpv6PeersAndPreservesHost()
    {
        await using var server = await HttpFixture.Start("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
        var resolver = new SequenceResolver([IPAddress.Loopback]);
        using var client = SafeClient.Create(resolver, server.Port, _ => true);

        using var response = await client.GetAsync("http://fixture.test/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Host: fixture.test", await server.Request);
        Assert.Equal(IPAddress.Loopback, SafeClient.LastPeerAddress);

        await using var ipv6Server = await HttpFixture.Start(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK",
            IPAddress.IPv6Loopback);
        using var ipv6Client = SafeClient.Create(new SequenceResolver([IPAddress.IPv6Loopback]), ipv6Server.Port, _ => true);
        using var ipv6Response = await ipv6Client.GetAsync("http://fixture-v6.test/");
        Assert.Equal(HttpStatusCode.OK, ipv6Response.StatusCode);
        Assert.Contains("Host: fixture-v6.test", await ipv6Server.Request);
        Assert.Equal(IPAddress.IPv6Loopback, SafeClient.LastPeerAddress);
    }

    [Fact]
    public async Task MixedAnswersAndRebindingAreRejectedBeforeBlockedListenerContact()
    {
        await using var allowed = await HttpFixture.Start("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await using var blocked = await HttpFixture.Start("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", IPAddress.Parse("127.0.0.2"), allowed.Port);
        var mixed = new SequenceResolver([IPAddress.Loopback, IPAddress.Parse("127.0.0.2")]);
        using var mixedClient = SafeClient.Create(mixed, allowed.Port, ip => ip.Equals(IPAddress.Loopback));
        await Assert.ThrowsAsync<HttpRequestException>(() => mixedClient.GetAsync("http://fixture.test/"));

        var rebinding = new SequenceResolver([IPAddress.Loopback], [IPAddress.Parse("127.0.0.2")]);
        using var firstClient = SafeClient.Create(rebinding, allowed.Port, ip => ip.Equals(IPAddress.Loopback));
        using (var first = await firstClient.GetAsync("http://fixture.test/"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }
        using var secondClient = SafeClient.Create(rebinding, allowed.Port, ip => ip.Equals(IPAddress.Loopback));
        await Assert.ThrowsAsync<HttpRequestException>(() => secondClient.GetAsync("http://fixture.test/"));
        Assert.False(blocked.WasContacted);
    }

    [Fact]
    public async Task RedirectsAreRevalidatedAndLoopsStopAtHopLimit()
    {
        await using var redirect = await HttpFixture.Start("HTTP/1.1 302 Found\r\nLocation: http://blocked.test/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var resolver = new HostResolver(new Dictionary<string, IPAddress[]>
        {
            ["allowed.test"] = [IPAddress.Loopback],
            ["blocked.test"] = [IPAddress.Parse("127.0.0.2")]
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => SafeClient.GetFollowingRedirects(
            new Uri($"http://allowed.test:{redirect.Port}/"), resolver, redirect.Port, ip => ip.Equals(IPAddress.Loopback), 10));

        await using var loop = await HttpFixture.Start("HTTP/1.1 302 Found\r\nLocation: /again\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", repeat: true);
        var loopResolver = new HostResolver(new Dictionary<string, IPAddress[]> { ["loop.test"] = [IPAddress.Loopback] });
        await Assert.ThrowsAsync<InvalidOperationException>(() => SafeClient.GetFollowingRedirects(
            new Uri($"http://loop.test:{loop.Port}/"), loopResolver, loop.Port, _ => true, 3));
        Assert.Equal(2, loop.ContactCount);

        await using var endless = await HttpFixture.Start(
            contact => $"HTTP/1.1 302 Found\r\nLocation: /hop-{contact}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            repeat: true);
        var endlessResolver = new HostResolver(new Dictionary<string, IPAddress[]> { ["endless.test"] = [IPAddress.Loopback] });
        await Assert.ThrowsAsync<InvalidOperationException>(() => SafeClient.GetFollowingRedirects(
            new Uri($"http://endless.test:{endless.Port}/"), endlessResolver, endless.Port, _ => true, 3));
        Assert.Equal(3, endless.ContactCount);
    }

    [Fact]
    public async Task ImplicitProxyIsIgnoredAndInvalidTlsRemainsFailedWithBoundedEvidence()
    {
        await using var origin = await HttpFixture.Start("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await using var proxy = await HttpFixture.Start("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var oldProxy = Environment.GetEnvironmentVariable("HTTP_PROXY");
        Environment.SetEnvironmentVariable("HTTP_PROXY", $"http://127.0.0.1:{proxy.Port}");
        try
        {
            using var client = SafeClient.Create(new SequenceResolver([IPAddress.Loopback]), origin.Port, _ => true);
            using var response = await client.GetAsync("http://fixture.test/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(proxy.WasContacted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", oldProxy);
        }

        await using var tls = await TlsFixture.Start();
        var evidence = new List<string>();
        using var tlsConnection = new TcpClient();
        await tlsConnection.ConnectAsync(IPAddress.Loopback, tls.Port);
        await using var ssl = new SslStream(tlsConnection.GetStream(), false, (_, certificate, _, errors) =>
        {
            var summary = $"subject={certificate?.Subject}; errors={errors}";
            evidence.Add(summary[..Math.Min(summary.Length, 256)]);
            return errors == SslPolicyErrors.None;
        });
        var failure = await Record.ExceptionAsync(() => ssl.AuthenticateAsClientAsync("fixture.test"));
        Assert.NotNull(failure);
        Assert.Single(evidence);
        Assert.DoesNotContain("PRIVATE KEY", evidence[0]);
        Assert.True(evidence[0].Length <= 256);
    }

    private interface IResolver
    {
        ValueTask<IPAddress[]> Resolve(string host, CancellationToken cancellationToken);
    }

    private sealed class SequenceResolver(params IPAddress[][] answers) : IResolver
    {
        private int _index;

        public ValueTask<IPAddress[]> Resolve(string host, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _index) - 1;
            return ValueTask.FromResult(answers[Math.Min(index, answers.Length - 1)]);
        }
    }

    private sealed class HostResolver(IReadOnlyDictionary<string, IPAddress[]> answers) : IResolver
    {
        public ValueTask<IPAddress[]> Resolve(string host, CancellationToken cancellationToken) => ValueTask.FromResult(answers[host]);
    }

    private static class SafeClient
    {
        public static IPAddress? LastPeerAddress { get; private set; }

        public static HttpClient Create(IResolver resolver, int fixturePort, Func<IPAddress, bool> permitted)
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(2),
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var addresses = await resolver.Resolve(context.DnsEndPoint.Host, cancellationToken);
                    if (addresses.Length == 0 || addresses.Any(ip => !permitted(Normalize(ip))))
                    {
                        throw new HttpRequestException("Destination policy rejected DNS answers.");
                    }

                    var socket = new Socket(addresses[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(addresses[0], fixturePort), cancellationToken);
                        var peer = (IPEndPoint?)socket.RemoteEndPoint;
                        if (peer is null || !peer.Address.Equals(addresses[0]))
                        {
                            throw new HttpRequestException("Connected peer did not match the selected address.");
                        }
                        LastPeerAddress = peer.Address;
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };

            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        }

        public static async Task GetFollowingRedirects(Uri uri, IResolver resolver, int port, Func<IPAddress, bool> permitted, int maxHops)
        {
            var visited = new HashSet<Uri>();
            for (var hop = 0; hop < maxHops; hop++)
            {
                if (!visited.Add(uri))
                {
                    throw new InvalidOperationException("Redirect loop detected.");
                }
                using var client = Create(resolver, port, permitted);
                using var response = await client.GetAsync(uri);
                if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is null)
                {
                    return;
                }
                uri = new Uri(uri, response.Headers.Location);
            }
            throw new InvalidOperationException("Redirect hop limit exceeded.");
        }

        private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private sealed class HttpFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<int, string> _response;
        private readonly bool _repeat;
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource<string> _request = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _server;
        private int _contacts;

        private HttpFixture(TcpListener listener, Func<int, string> response, bool repeat)
        {
            _listener = listener;
            _response = response;
            _repeat = repeat;
            _server = Serve();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public bool WasContacted => ContactCount > 0;
        public int ContactCount => Volatile.Read(ref _contacts);
        public Task<string> Request => _request.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public static Task<HttpFixture> Start(string response, IPAddress? address = null, int port = 0, bool repeat = false)
            => Start(_ => response, address, port, repeat);

        public static Task<HttpFixture> Start(Func<int, string> response, IPAddress? address = null, int port = 0, bool repeat = false)
        {
            var listener = new TcpListener(address ?? IPAddress.Loopback, port);
            listener.Start();
            return Task.FromResult(new HttpFixture(listener, response, repeat));
        }

        private async Task Serve()
        {
            try
            {
                do
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var contact = Interlocked.Increment(ref _contacts);
                    var stream = client.GetStream();
                    var buffer = new byte[4096];
                    var read = await stream.ReadAsync(buffer, _stop.Token);
                    _request.TrySetResult(Encoding.ASCII.GetString(buffer, 0, read));
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(_response(contact)), _stop.Token);
                } while (_repeat);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _server;
            _stop.Dispose();
        }
    }

    private sealed class TlsFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly X509Certificate2 _certificate;
        private readonly Task _server;

        private TlsFixture(TcpListener listener, X509Certificate2 certificate)
        {
            _listener = listener;
            _certificate = certificate;
            _server = Serve();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static Task<TlsFixture> Start()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=wrong.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("wrong.test");
            request.CertificateExtensions.Add(names.Build());
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new TlsFixture(listener, certificate));
        }

        private async Task Serve()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var ssl = new SslStream(client.GetStream());
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                });
            }
            catch (AuthenticationException)
            {
            }
            catch (IOException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _server;
            _certificate.Dispose();
        }
    }
}
