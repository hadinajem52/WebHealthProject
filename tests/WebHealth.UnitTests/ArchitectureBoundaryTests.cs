using System.Reflection;
using FluentAssertions;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] ForbiddenDomainDependencies =
    [
        "Hangfire.Core",
        "Microsoft.AspNetCore.Mvc.Core",
        "Microsoft.EntityFrameworkCore",
        "Npgsql"
    ];

    [Theory]
    [InlineData("WebHealth.Domain")]
    [InlineData("WebHealth.Application")]
    public void InnerLayer_DoesNotReferenceInfrastructureFrameworks(string assemblyName)
    {
        var references = Assembly.Load(assemblyName)
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        references.Should().NotContain(ForbiddenDomainDependencies);
    }
}
