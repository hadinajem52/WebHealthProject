using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Monitoring;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class SafeHttpTransportTests
{
    [Fact]
    public async Task SendAsync_PreservesHostAndUserAgentAndBoundsDecodedBody()
    {
        await using var server = await HttpFixture.Start(
            "HTTP/1.1 200 OK\r\nContent-Length: 12\r\nConnection: close\r\n\r\nHello world!");
        await using var harness = CreateHarness(
            new HostResolver(("allowed.test", [IPAddress.Loopback])),
            (_, host, _, _, _) => Task.FromResult(host == "allowed.test"));

        var result = await harness.Transport.SendAsync(
            new(
                Guid.NewGuid(),
                $"http://allowed.test:{server.Port}/health?token=not-logged",
                false,
                MaxResponseBodyBytes: 8));

        result.Succeeded.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.BodyTruncated.Should().BeTrue();
        result.ResponseBytesRead.Should().Be(9);
        Encoding.UTF8.GetString(result.Body.Span).Should().Be("Hello wo");
        result.FinalDestination.Should().Be(new SafeHttpDestination("http", "allowed.test", server.Port));
        var request = await server.Request;
        request.Should().Contain($"Host: allowed.test:{server.Port}");
        request.Should().Contain("User-Agent: WebHealthMonitor/1.0");
        request.Should().NotContain("Authorization:");
        request.Should().NotContain("Cookie:");
    }

    [Fact]
    public async Task SendAsync_DoesNotTruncateBodyAtTheExactConfiguredLimit()
    {
        await using var server = await HttpFixture.Start(
            "HTTP/1.1 200 OK\r\nContent-Length: 8\r\nConnection: close\r\n\r\n12345678");
        await using var harness = CreateHarness(
            new HostResolver(("bounded.test", [IPAddress.Loopback])), AuthorizeAll);

        var result = await harness.Transport.SendAsync(new(
            Guid.NewGuid(),
            $"http://bounded.test:{server.Port}/",
            false,
            MaxResponseBodyBytes: 8));

        result.Succeeded.Should().BeTrue();
        result.BodyTruncated.Should().BeFalse();
        result.ResponseBytesRead.Should().Be(8);
        result.Body.Length.Should().Be(8);
    }

    [Fact]
    public async Task SendAsync_RejectsMixedDnsAnswersBeforeContactingTarget()
    {
        await using var server = await HttpFixture.Start(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await using var harness = CreateHarness(
            new HostResolver(("mixed.test", [IPAddress.Loopback, IPAddress.Parse("10.0.0.1")])),
            AuthorizeAll,
            addressPolicy: new ExactLoopbackPolicy());

        var result = await harness.Transport.SendAsync(
            new(Guid.NewGuid(), $"http://mixed.test:{server.Port}/", false));

        result.Failure.Should().Be(SafeHttpFailureKind.DestinationRejected);
        server.ContactCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_RechecksDnsForEveryConnectionAndRejectsRebinding()
    {
        await using var server = await HttpFixture.Start(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            repeat: true);
        await using var harness = CreateHarness(
            new SequenceResolver([IPAddress.Loopback], [IPAddress.Parse("10.0.0.1")]),
            AuthorizeAll,
            addressPolicy: new ExactLoopbackPolicy());
        var request = new SafeHttpTransportRequest(
            Guid.NewGuid(), $"http://rebind.test:{server.Port}/", false);

        var first = await harness.Transport.SendAsync(request);
        var second = await harness.Transport.SendAsync(request);

        first.Succeeded.Should().BeTrue();
        second.Failure.Should().Be(SafeHttpFailureKind.DestinationRejected);
        server.ContactCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_ReauthorizesRedirectDestinationsBeforeConnecting()
    {
        var redirectPort = 0;
        await using var server = await HttpFixture.Start(
            contact => contact == 1
                ? $"HTTP/1.1 302 Found\r\nLocation: http://blocked.test:{redirectPort}/next\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                : "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            repeat: true);
        redirectPort = server.Port;
        await using var harness = CreateHarness(
            new HostResolver(
                ("allowed.test", [IPAddress.Loopback]),
                ("blocked.test", [IPAddress.Loopback])),
            (_, host, _, _, _) => Task.FromResult(host == "allowed.test"));

        var result = await harness.Transport.SendAsync(
            new(Guid.NewGuid(), $"http://allowed.test:{server.Port}/", false));

        result.Failure.Should().Be(SafeHttpFailureKind.TargetNotAuthorized);
        server.ContactCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_DetectsRedirectLoops()
    {
        await using var loop = await HttpFixture.Start(
            contact => contact == 1
                ? "HTTP/1.1 302 Found\r\nLocation: /path/../again?q=%41\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                : "HTTP/1.1 302 Found\r\nLocation: /again?q=A\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            repeat: true);
        await using var loopHarness = CreateHarness(
            new HostResolver(("loop.test", [IPAddress.Loopback])), AuthorizeAll);

        var loopResult = await loopHarness.Transport.SendAsync(
            new(Guid.NewGuid(), $"http://loop.test:{loop.Port}/", false));

        loopResult.Failure.Should().Be(SafeHttpFailureKind.RedirectLoop);
        loopResult.StatusCode.Should().Be(302);
        loopResult.Redirects.Should().HaveCount(2);
        loopResult.Redirects[^1].IsLoop.Should().BeTrue();
        loopResult.Redirects.Should().OnlyContain(hop =>
            !hop.FromUrl.Contains('?') && !hop.ToUrl.Contains('?'));
        loop.ContactCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_AllowsExactlyTheConfiguredRedirectCount()
    {
        await using var allowed = await HttpFixture.Start(
            contact => contact <= 10
                ? $"HTTP/1.1 302 Found\r\nLocation: /hop-{contact}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                : "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK",
            repeat: true);
        await using var allowedHarness = CreateHarness(
            new HostResolver(("exact.test", [IPAddress.Loopback])), AuthorizeAll);

        var success = await allowedHarness.Transport.SendAsync(
            new(Guid.NewGuid(), $"http://exact.test:{allowed.Port}/", false, MaxRedirects: 10));

        success.Succeeded.Should().BeTrue();
        success.Redirects.Should().HaveCount(10);
        allowed.ContactCount.Should().Be(11);

        await using var excessive = await HttpFixture.Start(
            contact => $"HTTP/1.1 302 Found\r\nLocation: /hop-{contact}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            repeat: true);
        await using var excessiveHarness = CreateHarness(
            new HostResolver(("excessive.test", [IPAddress.Loopback])), AuthorizeAll);

        var failure = await excessiveHarness.Transport.SendAsync(
            new(Guid.NewGuid(), $"http://excessive.test:{excessive.Port}/", false, MaxRedirects: 10));

        failure.Failure.Should().Be(SafeHttpFailureKind.RedirectLimit);
        failure.StatusCode.Should().Be(302);
        failure.Redirects.Should().HaveCount(10);
        excessive.ContactCount.Should().Be(11);
    }

    [Fact]
    public async Task SendAsync_StopsAtRedirectLimitAndRejectsUnsupportedRedirects()
    {
        await using var endless = await HttpFixture.Start(
            contact => $"HTTP/1.1 302 Found\r\nLocation: /hop-{contact}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            repeat: true);
        await using var limitedHarness = CreateHarness(
            new HostResolver(("redirect.test", [IPAddress.Loopback])),
            AuthorizeAll);

        var limited = await limitedHarness.Transport.SendAsync(
            new(Guid.NewGuid(), $"http://redirect.test:{endless.Port}/", false, MaxRedirects: 1));

        limited.Failure.Should().Be(SafeHttpFailureKind.RedirectLimit);
        limited.Redirects.Should().ContainSingle();
        endless.ContactCount.Should().Be(2);

        await using var invalid = await HttpFixture.Start(
            "HTTP/1.1 302 Found\r\nLocation: ftp://redirect.test/file\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await using var invalidHarness = CreateHarness(
            new HostResolver(("redirect.test", [IPAddress.Loopback])), AuthorizeAll);
        var invalidResult = await invalidHarness.Transport.SendAsync(
            new(Guid.NewGuid(), $"http://redirect.test:{invalid.Port}/", false));

        invalidResult.Failure.Should().Be(SafeHttpFailureKind.RedirectInvalid);
    }

    [Fact]
    public async Task SendAsync_RejectsOversizedResponseHeaders()
    {
        var header = new string('a', 33 * 1024);
        await using var server = await HttpFixture.Start(
            $"HTTP/1.1 200 OK\r\nX-Large: {header}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await using var harness = CreateHarness(
            new HostResolver(("headers.test", [IPAddress.Loopback])), AuthorizeAll);

        var result = await harness.Transport.SendAsync(
            new(Guid.NewGuid(), $"http://headers.test:{server.Port}/", false));

        result.Failure.Should().Be(SafeHttpFailureKind.ResponseHeadersTooLarge);
    }

    [Fact]
    public void ConnectionFactory_DisablesRedirectsProxiesCookiesAndTlsOverrides()
    {
        var options = DefaultOptions();
        var handler = SafeHttpConnectionFactory.Create(
            new HostResolver(("unused.test", [IPAddress.Loopback])),
            new ExactLoopbackPolicy(),
            new SafeHttpConcurrencyLimiter(options),
            options);

        handler.AllowAutoRedirect.Should().BeFalse();
        handler.UseProxy.Should().BeFalse();
        handler.UseCookies.Should().BeFalse();
        handler.SslOptions.RemoteCertificateValidationCallback.Should().BeNull();
        handler.MaxResponseHeadersLength.Should().Be(32);
        handler.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(5));
        handler.Dispose();
    }

    [Fact]
    public async Task SendAsync_RejectsProductionHttpsDowngradeBeforeFollowingIt()
    {
        using var client = new HttpClient(new RedirectHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        var options = DefaultOptions();
        var transport = new SafeHttpTransport(
            new SingleClientFactory(client),
            new DelegateAuthorizer(AuthorizeAll),
            new SafeHttpConcurrencyLimiter(options),
            options,
            TimeProvider.System);

        var result = await transport.SendAsync(
            new(Guid.NewGuid(), "https://allowed.test/start", true));

        result.Failure.Should().Be(SafeHttpFailureKind.HttpsDowngrade);
    }

    [Fact]
    public async Task SendAsync_KeepsTlsValidationEnabled()
    {
        await using var server = await TlsFixture.Start();
        await using var harness = CreateHarness(
            new HostResolver(("allowed.test", [IPAddress.Loopback])), AuthorizeAll);

        var result = await harness.Transport.SendAsync(
            new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/", true));

        result.Failure.Should().Be(SafeHttpFailureKind.Tls);
    }

    [Fact]
    public async Task SendAsync_EnforcesWholeRequestTimeoutAndCallerCancellation()
    {
        await using var server = await HttpFixture.Start(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            delay: TimeSpan.FromSeconds(2),
            repeat: true);
        await using var harness = CreateHarness(
            new HostResolver(("slow.test", [IPAddress.Loopback])),
            AuthorizeAll,
            options => options with { Timeout = TimeSpan.FromMilliseconds(100) });

        var timeout = await harness.Transport.SendAsync(
            new(Guid.NewGuid(), $"http://slow.test:{server.Port}/", false));
        timeout.Failure.Should().Be(SafeHttpFailureKind.Timeout);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var cancelled = await harness.Transport.SendAsync(
            new(Guid.NewGuid(), $"http://slow.test:{server.Port}/", false), cancellation.Token);
        cancelled.Failure.Should().Be(SafeHttpFailureKind.Cancelled);
    }

    [Theory]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Length: 10\r\nConnection: close\r\n\r\nabc")]
    [InlineData("HTTP/1.1 200 OK\r\nContent-Length: 8\r\nContent-Encoding: gzip\r\nConnection: close\r\n\r\nnot-gzip")]
    public async Task SendAsync_ClassifiesUntrustedResponseReadFailures(string response)
    {
        await using var server = await HttpFixture.Start(response);
        await using var harness = CreateHarness(
            new HostResolver(("broken.test", [IPAddress.Loopback])), AuthorizeAll);

        var result = await harness.Transport.SendAsync(
            new(Guid.NewGuid(), $"http://broken.test:{server.Port}/", false));

        result.Failure.Should().Be(SafeHttpFailureKind.Protocol);
    }

    [Fact]
    public async Task SendAsync_ReusesAndDisposesOneFactoryClientAcrossRedirects()
    {
        var handler = new RedirectThenSuccessHandler();
        var client = new TrackingHttpClient(handler);
        var factory = new TrackingClientFactory(client);
        var options = DefaultOptions();
        var transport = new SafeHttpTransport(
            factory,
            new DelegateAuthorizer(AuthorizeAll),
            new SafeHttpConcurrencyLimiter(options),
            options,
            TimeProvider.System);

        var result = await transport.SendAsync(
            new(Guid.NewGuid(), "http://allowed.test/start", false));

        result.Succeeded.Should().BeTrue();
        handler.RequestCount.Should().Be(2);
        factory.CreateCount.Should().Be(1);
        client.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrencyLimiter_QueuesAboveConfiguredGlobalHostAndAddressBounds()
    {
        var options = DefaultOptions() with
        {
            GlobalConcurrency = 1,
            PerHostConcurrency = 1,
            PerIpConcurrency = 1
        };
        var limiter = new SafeHttpConcurrencyLimiter(options);

        using var global = await limiter.AcquireGlobalAsync(CancellationToken.None);
        var waitingGlobal = limiter.AcquireGlobalAsync(CancellationToken.None).AsTask();
        await AssertStillWaiting(waitingGlobal);
        global.Dispose();
        using var acquiredGlobal = await waitingGlobal;

        using var host = await limiter.AcquireHostAsync("example.test", CancellationToken.None);
        var waitingHost = limiter.AcquireHostAsync("example.test", CancellationToken.None).AsTask();
        await AssertStillWaiting(waitingHost);
        host.Dispose();
        using var acquiredHost = await waitingHost;

        using var address = await limiter.AcquireAddressAsync("1.1.1.1", CancellationToken.None);
        var waitingAddress = limiter.AcquireAddressAsync("1.1.1.1", CancellationToken.None).AsTask();
        await AssertStillWaiting(waitingAddress);
        address.Dispose();
        using var acquiredAddress = await waitingAddress;
    }

    private static async Task AssertStillWaiting(Task task)
    {
        await Task.Delay(50);
        task.IsCompleted.Should().BeFalse();
    }

    private static Task<bool> AuthorizeAll(
        Guid endpointId, string host, int port, DateTimeOffset at, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    private static TransportHarness CreateHarness(
        IMonitoringDnsResolver resolver,
        Func<Guid, string, int, DateTimeOffset, CancellationToken, Task<bool>> authorize,
        Func<SafeHttpTransportOptions, SafeHttpTransportOptions>? configure = null,
        IDestinationAddressPolicy? addressPolicy = null)
    {
        var options = configure?.Invoke(DefaultOptions()) ?? DefaultOptions();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton(resolver);
        services.AddSingleton<IMonitoringDnsResolver>(resolver);
        services.AddSingleton<IDestinationAddressPolicy>(addressPolicy ?? new ExactLoopbackPolicy());
        services.AddSingleton<SafeHttpConcurrencyLimiter>();
        services.AddSingleton<IMonitoringTargetAuthorizer>(new DelegateAuthorizer(authorize));
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient(SafeHttpTransportOptions.ClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(provider => SafeHttpConnectionFactory.Create(
                provider.GetRequiredService<IMonitoringDnsResolver>(),
                provider.GetRequiredService<IDestinationAddressPolicy>(),
                provider.GetRequiredService<SafeHttpConcurrencyLimiter>(),
                options));
        services.AddScoped<ISafeHttpTransport, SafeHttpTransport>();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        return new(provider, scope, scope.ServiceProvider.GetRequiredService<ISafeHttpTransport>());
    }

    private static SafeHttpTransportOptions DefaultOptions() => new();

    private sealed record DelegateAuthorizer(
        Func<Guid, string, int, DateTimeOffset, CancellationToken, Task<bool>> Authorize)
        : IMonitoringTargetAuthorizer
    {
        public Task<bool> IsAuthorizedAsync(
            Guid endpointId, string normalizedHost, int port, DateTimeOffset at,
            CancellationToken cancellationToken = default) =>
            Authorize(endpointId, normalizedHost, port, at, cancellationToken);
    }

    private sealed class ExactLoopbackPolicy : IDestinationAddressPolicy
    {
        public bool IsAllowed(IPAddress address) =>
            address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.IPv6Loopback);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TrackingClientFactory(TrackingHttpClient client) : IHttpClientFactory
    {
        public int CreateCount { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreateCount++;
            return client;
        }
    }

    private sealed class TrackingHttpClient(HttpMessageHandler handler) : HttpClient(handler)
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
        }
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                RequestMessage = request,
                Content = new ByteArrayContent([])
            };
            response.Headers.Location = new Uri("http://allowed.test/next");
            return Task.FromResult(response);
        }
    }

    private sealed class RedirectThenSuccessHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(
                RequestCount == 1 ? HttpStatusCode.Redirect : HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent([])
            };
            if (RequestCount == 1)
            {
                response.Headers.Location = new Uri("/final", UriKind.Relative);
            }
            return Task.FromResult(response);
        }
    }

    private sealed class HostResolver(params (string Host, IPAddress[] Addresses)[] answers)
        : IMonitoringDnsResolver
    {
        private readonly Dictionary<string, IPAddress[]> _answers =
            answers.ToDictionary(answer => answer.Host, answer => answer.Addresses, StringComparer.OrdinalIgnoreCase);

        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>(_answers[host]);
    }

    private sealed class SequenceResolver(params IPAddress[][] answers) : IMonitoringDnsResolver
    {
        private int _index;

        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host, CancellationToken cancellationToken = default)
        {
            var index = Interlocked.Increment(ref _index) - 1;
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>(
                answers[Math.Min(index, answers.Length - 1)]);
        }
    }

    private sealed record TransportHarness(
        ServiceProvider Provider,
        AsyncServiceScope Scope,
        ISafeHttpTransport Transport) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Scope.DisposeAsync();
            await Provider.DisposeAsync();
        }
    }

    private sealed class HttpFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<int, string> _response;
        private readonly TimeSpan _delay;
        private readonly bool _repeat;
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource<string> _request = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _server;
        private int _contacts;

        private HttpFixture(TcpListener listener, Func<int, string> response, TimeSpan delay, bool repeat)
        {
            _listener = listener;
            _response = response;
            _delay = delay;
            _repeat = repeat;
            _server = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int ContactCount => Volatile.Read(ref _contacts);
        public Task<string> Request => _request.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public static Task<HttpFixture> Start(
            string response, TimeSpan? delay = null, bool repeat = false) =>
            Start(_ => response, delay, repeat);

        public static Task<HttpFixture> Start(
            Func<int, string> response, TimeSpan? delay = null, bool repeat = false)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new HttpFixture(listener, response, delay ?? TimeSpan.Zero, repeat));
        }

        private async Task ServeAsync()
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
                    await Task.Delay(_delay, _stop.Token);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(_response(contact)), _stop.Token);
                } while (_repeat);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (IOException)
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
            _server = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static Task<TlsFixture> Start()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=wrong.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("wrong.test");
            request.CertificateExtensions.Add(names.Build());
            using var generated = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var certificate = X509CertificateLoader.LoadPkcs12(
                generated.Export(X509ContentType.Pfx), null);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new TlsFixture(listener, certificate));
        }

        private async Task ServeAsync()
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
