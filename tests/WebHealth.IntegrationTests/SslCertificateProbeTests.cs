using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Monitoring;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// Controlled-TLS coverage for the certificate probe: every category BR-C03 requires is
/// produced against a real handshake, and every one of them is produced without the probe ever
/// accepting the certificate (BR-Q04).
/// </summary>
public sealed class SslCertificateProbeTests
{
    [Fact]
    public async Task ProbeAsync_RecordsCertificateEvidenceForAValidWindowedCertificate()
    {
        using var certificate = TestCertificates.SelfSigned(
            "CN=allowed.test", "allowed.test", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(60));
        await using var server = await TlsServerFixture.Start(certificate);
        var probe = CreateProbe();

        var result = await probe.ProbeAsync(
            new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/"));

        result.Succeeded.Should().BeTrue();
        var observed = result.Certificate.Should().NotBeNull().And.Subject.As<TlsCertificateObservation>();
        observed.Subject.Should().Be("CN=allowed.test");
        observed.Issuer.Should().Be("CN=allowed.test");
        observed.SerialNumber.Should().Be(certificate.SerialNumber);
        observed.Sha256Fingerprint.Should().Be(
            Convert.ToHexStringLower(certificate.GetCertHash(HashAlgorithmName.SHA256)));
        observed.NotBefore.Should().BeCloseTo(
            new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero), TimeSpan.FromSeconds(1));
        observed.NotAfter.Should().BeCloseTo(
            new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero), TimeSpan.FromSeconds(1));
        observed.SubjectAlternativeNames.Should().Equal("allowed.test");
        observed.HostnameMatched.Should().BeTrue();

        // Self-signed: the hostname matches and the dates are fine, so the only remaining
        // problem is trust.
        observed.ChainTrusted.Should().BeFalse();
        observed.ValidationCategory.Should().Be(TlsValidationCategory.Untrusted);
    }

    [Fact]
    public async Task ProbeAsync_ReportsExpiredCertificates()
    {
        using var certificate = TestCertificates.SelfSigned(
            "CN=allowed.test", "allowed.test", DateTimeOffset.UtcNow.AddDays(-40), DateTimeOffset.UtcNow.AddDays(-1));
        await using var server = await TlsServerFixture.Start(certificate);

        var result = await CreateProbe().ProbeAsync(
            new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/"));

        result.Succeeded.Should().BeTrue();
        result.Certificate!.ValidationCategory.Should().Be(TlsValidationCategory.Expired);
        result.Certificate.NotAfter.Should().BeBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ProbeAsync_ReportsNotYetValidCertificates()
    {
        using var certificate = TestCertificates.SelfSigned(
            "CN=allowed.test", "allowed.test", DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(60));
        await using var server = await TlsServerFixture.Start(certificate);

        var result = await CreateProbe().ProbeAsync(
            new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/"));

        result.Succeeded.Should().BeTrue();
        result.Certificate!.ValidationCategory.Should().Be(TlsValidationCategory.NotYetValid);
    }

    [Fact]
    public async Task ProbeAsync_ReportsHostnameMismatchWithTheObservedNames()
    {
        using var certificate = TestCertificates.SelfSigned(
            "CN=wrong.test", "wrong.test", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(60));
        await using var server = await TlsServerFixture.Start(certificate);

        var result = await CreateProbe().ProbeAsync(
            new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/"));

        result.Succeeded.Should().BeTrue();
        result.Certificate!.ValidationCategory.Should().Be(TlsValidationCategory.HostnameMismatch);
        result.Certificate.HostnameMatched.Should().BeFalse();
        result.Certificate.SubjectAlternativeNames.Should().Equal("wrong.test");
    }

    [Fact]
    public async Task ProbeAsync_SendsNoApplicationDataOverTheProbeConnection()
    {
        using var certificate = TestCertificates.SelfSigned(
            "CN=allowed.test", "allowed.test", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(60));
        await using var server = await TlsServerFixture.Start(certificate);

        var result = await CreateProbe().ProbeAsync(
            new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/"));

        result.Succeeded.Should().BeTrue();
        await server.Completed;
        server.ContactCount.Should().Be(1);

        // Under TLS 1.3 the server can consider its own side of the handshake complete before
        // the client's rejection alert arrives, so the server-side handshake result proves
        // nothing. What matters is that the client rejected the certificate and therefore
        // never sent a single application byte over the connection.
        server.ApplicationDataObserved.Should().BeFalse();
    }

    [Fact]
    public async Task ProbeAsync_ReportsHandshakeFailureWhenTheTargetDropsTheConnection()
    {
        // The socket connected, so the failure belongs to the handshake phase even though the
        // platform surfaces it as a dropped stream rather than an authentication error.
        await using var server = await TlsServerFixture.StartClosing();

        var result = await CreateProbe().ProbeAsync(
            new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/"));

        result.Failure.Should().Be(SslProbeFailureKind.HandshakeFailed);
        result.Certificate.Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_ReportsConnectionFailureWhenNothingIsListening()
    {
        var closedPort = FindClosedPort();

        var result = await CreateProbe().ProbeAsync(
            new(Guid.NewGuid(), $"https://allowed.test:{closedPort}/"));

        result.Failure.Should().Be(SslProbeFailureKind.Connection);
        result.Certificate.Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_ReportsHandshakeFailureWhenNoCertificateIsPresented()
    {
        // A server that refuses the handshake outright — no matching cipher or protocol, or
        // an unknown SNI name — never presents a certificate to categorise.
        await using var server = await TlsServerFixture.StartRefusingHandshake();

        var result = await CreateProbe().ProbeAsync(
            new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/"));

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(SslProbeFailureKind.HandshakeFailed);
        result.Certificate.Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_AppliesDestinationPolicyBeforeContactingTheTarget()
    {
        using var certificate = TestCertificates.SelfSigned(
            "CN=allowed.test", "allowed.test", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(60));
        await using var server = await TlsServerFixture.Start(certificate);
        var probe = CreateProbe(
            resolver: new HostResolver(("allowed.test", [IPAddress.Loopback, IPAddress.Parse("10.0.0.1")])));

        var result = await probe.ProbeAsync(new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/"));

        result.Failure.Should().Be(SslProbeFailureKind.DestinationRejected);
        server.ContactCount.Should().Be(0);
    }

    [Fact]
    public async Task ProbeAsync_RequiresTargetAuthorizationBeforeContactingTheTarget()
    {
        using var certificate = TestCertificates.SelfSigned(
            "CN=allowed.test", "allowed.test", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(60));
        await using var server = await TlsServerFixture.Start(certificate);
        var probe = CreateProbe(authorize: false);

        var result = await probe.ProbeAsync(new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/"));

        result.Failure.Should().Be(SslProbeFailureKind.TargetNotAuthorized);
        server.ContactCount.Should().Be(0);
    }

    [Fact]
    public async Task ProbeAsync_RejectsHttpOnlyEndpointsWithoutConnecting()
    {
        using var certificate = TestCertificates.SelfSigned(
            "CN=allowed.test", "allowed.test", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(60));
        await using var server = await TlsServerFixture.Start(certificate);

        var result = await CreateProbe().ProbeAsync(
            new(Guid.NewGuid(), $"http://allowed.test:{server.Port}/"));

        result.Failure.Should().Be(SslProbeFailureKind.NotHttps);
        result.Certificate.Should().BeNull();
        server.ContactCount.Should().Be(0);
    }

    [Fact]
    public async Task ProbeAsync_RejectsUnusableUrls()
    {
        var result = await CreateProbe().ProbeAsync(new(Guid.NewGuid(), "not-a-url"));

        result.Failure.Should().Be(SslProbeFailureKind.InvalidUrl);
    }

    [Fact]
    public async Task ProbeAsync_ReportsCallerCancellationSeparatelyFromTimeout()
    {
        await using var server = await TlsServerFixture.StartSilent();
        using var cancellation = new CancellationTokenSource();

        var probing = CreateProbe().ProbeAsync(
            new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/"),
            cancellation.Token);
        await server.FirstContact;
        await cancellation.CancelAsync();

        (await probing).Failure.Should().Be(SslProbeFailureKind.Cancelled);
    }

    [Fact]
    public async Task ProbeAsync_EnforcesItsOwnTimeoutOnAStalledHandshake()
    {
        await using var server = await TlsServerFixture.StartSilent();

        var result = await CreateProbe().ProbeAsync(
            new(Guid.NewGuid(), $"https://allowed.test:{server.Port}/", TimeoutSeconds: 1));

        result.Failure.Should().Be(SslProbeFailureKind.Timeout);
    }

    private static int FindClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static ISslCertificateProbe CreateProbe(
        IMonitoringDnsResolver? resolver = null,
        bool authorize = true)
    {
        var options = new SafeHttpTransportOptions();
        return new SslCertificateProbe(
            resolver ?? new HostResolver(("allowed.test", [IPAddress.Loopback])),
            new ExactLoopbackPolicy(),
            new DelegateAuthorizer(authorize),
            new SafeHttpConcurrencyLimiter(options),
            options,
            TimeProvider.System);
    }

    private sealed record DelegateAuthorizer(bool Authorized) : IMonitoringTargetAuthorizer
    {
        public Task<bool> IsAuthorizedAsync(
            Guid endpointId, string normalizedHost, int port, DateTimeOffset at,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Authorized);
    }

    private sealed class ExactLoopbackPolicy : IDestinationAddressPolicy
    {
        public bool IsAllowed(IPAddress address) =>
            address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.IPv6Loopback);
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

    private enum ServerBehavior
    {
        PresentCertificate,
        CloseImmediately,
        RefuseHandshake,
        StaySilent
    }

    private sealed class TlsServerFixture : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly X509Certificate2? _certificate;
        private readonly ServerBehavior _behavior;
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _firstContact =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _server;
        private int _contacts;

        private TlsServerFixture(TcpListener listener, X509Certificate2? certificate, ServerBehavior behavior)
        {
            _listener = listener;
            _certificate = certificate;
            _behavior = behavior;
            _server = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int ContactCount => Volatile.Read(ref _contacts);
        public bool ApplicationDataObserved { get; private set; }
        public Task FirstContact => _firstContact.Task.WaitAsync(TimeSpan.FromSeconds(5));
        public Task Completed => _server.WaitAsync(TimeSpan.FromSeconds(5));

        public static Task<TlsServerFixture> Start(X509Certificate2 certificate) =>
            Start(certificate, ServerBehavior.PresentCertificate);

        public static Task<TlsServerFixture> StartClosing() =>
            Start(null, ServerBehavior.CloseImmediately);

        public static Task<TlsServerFixture> StartRefusingHandshake() =>
            Start(null, ServerBehavior.RefuseHandshake);

        public static Task<TlsServerFixture> StartSilent() =>
            Start(null, ServerBehavior.StaySilent);

        private static Task<TlsServerFixture> Start(X509Certificate2? certificate, ServerBehavior behavior)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new TlsServerFixture(listener, certificate, behavior));
        }

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _contacts);
                _firstContact.TrySetResult();

                if (_behavior is ServerBehavior.CloseImmediately)
                {
                    client.Close();
                    return;
                }

                if (_behavior is ServerBehavior.RefuseHandshake)
                {
                    // A fatal TLS "handshake_failure" alert, which is what a real server sends
                    // when it cannot agree on a protocol, cipher or name.
                    await client.GetStream().WriteAsync(
                        new byte[] { 0x15, 0x03, 0x03, 0x00, 0x02, 0x02, 0x28 }, _stop.Token);
                    return;
                }

                if (_behavior is ServerBehavior.StaySilent)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
                    return;
                }

                await using var ssl = new SslStream(client.GetStream());
                await ssl.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                    },
                    _stop.Token);

                var buffer = new byte[1024];
                ApplicationDataObserved = await ssl.ReadAsync(buffer, _stop.Token) > 0;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (AuthenticationException)
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
            await _stop.CancelAsync();
            _listener.Stop();
            await _server;
            _stop.Dispose();
        }
    }

    private static class TestCertificates
    {
        public static X509Certificate2 SelfSigned(
            string subject,
            string dnsName,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(dnsName);
            request.CertificateExtensions.Add(names.Build());
            using var generated = request.CreateSelfSigned(notBefore, notAfter);
            return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        }
    }
}
