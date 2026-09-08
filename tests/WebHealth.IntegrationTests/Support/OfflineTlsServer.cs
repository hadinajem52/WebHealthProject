using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace WebHealth.IntegrationTests.Support;

internal sealed class OfflineTlsServer : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _directory;
    private readonly Task<string> _errors;

    private OfflineTlsServer(Process process, string directory, int port)
    {
        _process = process;
        _directory = directory;
        Port = port;
        _errors = process.StandardError.ReadToEndAsync();
    }

    public int Port { get; }

    public static async Task<OfflineTlsServer> StartAsync(X509Certificate2 certificate)
    {
        var directory = Directory.CreateTempSubdirectory("webhealth-offline-tls-").FullName;
        try
        {
            var certificatePath = Path.Combine(directory, "certificate.pem");
            var keyPath = Path.Combine(directory, "key.pem");
            await File.WriteAllTextAsync(certificatePath, certificate.ExportCertificatePem());
            using var key = certificate.GetRSAPrivateKey()!;
            await File.WriteAllTextAsync(keyPath, key.ExportPkcs8PrivateKeyPem());
            int port;
            using (var reservation = new TcpListener(IPAddress.Loopback, 0))
            {
                reservation.Start();
                port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            }
            var gitOpenSsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin", "openssl.exe");
            var start = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("WEBHEALTH_TEST_OPENSSL")
                    ?? (OperatingSystem.IsWindows() && File.Exists(gitOpenSsl) ? gitOpenSsl : "openssl"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            foreach (var argument in new[] { "s_server", "-accept", $"127.0.0.1:{port}", "-cert", certificatePath,
                "-key", keyPath, "-naccept", "1", "-no_cache", "-www" })
            {
                start.ArgumentList.Add(argument);
            }
            var server = new OfflineTlsServer(Process.Start(start)!, directory, port);
            try
            {
                await server.WaitUntilReadyAsync().WaitAsync(TimeSpan.FromSeconds(10));
                return server;
            }
            catch
            {
                await server.DisposeAsync();
                throw;
            }
        }
        catch
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
            throw;
        }
    }

    private async Task WaitUntilReadyAsync()
    {
        while (await _process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (line == "ACCEPT")
            {
                return;
            }
        }
        throw new InvalidOperationException("OpenSSL test server did not start: " + await _errors);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
        await _process.WaitForExitAsync();
        _process.Dispose();
        Directory.Delete(_directory, true);
    }
}
