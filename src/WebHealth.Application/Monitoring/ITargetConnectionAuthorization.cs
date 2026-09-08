namespace WebHealth.Application.Monitoring;

public interface ITargetConnectionAuthorization
{
    Task<bool> IsAuthorizedAsync(Guid endpointId, string host, int port, CancellationToken cancellationToken);
}
