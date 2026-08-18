using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class TargetAuthorizationTrackingTests
{
    [Fact]
    public void NewEvidence_OnTrackedEndpoint_IsInserted()
    {
        using var context = CreateContext();
        var endpoint = AttachEndpoint(context);
        var evidence = CreateEvidence(endpoint.Id);

        endpoint.TargetAuthorizations.Add(evidence);
        context.ChangeTracker.DetectChanges();

        // A store-generated key convention would make this Modified, saving the new
        // evidence as an UPDATE that matches no row and fails the concurrency check.
        Assert.Equal(EntityState.Added, context.Entry(evidence).State);
    }

    [Fact]
    public void EvidenceKey_IsAssignedByApplicationCode()
    {
        using var context = CreateContext();
        var key = context.Model.FindEntityType(typeof(TargetAuthorizationEvidence))!
            .FindProperty(nameof(TargetAuthorizationEvidence.Id))!;

        Assert.Equal(ValueGenerated.Never, key.ValueGenerated);
    }

    private static Endpoint AttachEndpoint(ApplicationDbContext context)
    {
        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            EnvironmentId = Guid.NewGuid(),
            DisplayUrl = "https://example.test/",
            NormalizedUrl = "https://example.test/",
            NormalizedUrlHash = new byte[32],
            NormalizedHost = "example.test",
            EffectivePort = 443,
            NormalizationVersion = 1,
            Version = 1
        };
        context.Attach(endpoint);
        return endpoint;
    }

    private static TargetAuthorizationEvidence CreateEvidence(Guid endpointId) => new()
    {
        Id = Guid.NewGuid(),
        EndpointId = endpointId,
        AuthorizationKind = "Owned",
        EvidenceReference = "domain owned by me",
        NormalizedHost = "example.test",
        Port = 443,
        EffectiveFrom = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
        Version = 1
    };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=target_authorization_model;Username=webhealth")
            .Options;
        return new ApplicationDbContext(options);
    }
}
