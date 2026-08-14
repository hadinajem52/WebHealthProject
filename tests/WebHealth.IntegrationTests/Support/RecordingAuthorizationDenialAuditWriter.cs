using System.Collections.Concurrent;
using WebHealth.Application.Auditing;

namespace WebHealth.IntegrationTests.Support;

internal sealed class RecordingAuthorizationDenialAuditWriter : IAuthorizationDenialAuditWriter
{
    private readonly ConcurrentQueue<AuthorizationDenialAuditEntry> entries = new();

    public IReadOnlyCollection<AuthorizationDenialAuditEntry> Entries => entries.ToArray();

    public Task WriteAsync(
        AuthorizationDenialAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        entries.Enqueue(entry);
        return Task.CompletedTask;
    }
}
