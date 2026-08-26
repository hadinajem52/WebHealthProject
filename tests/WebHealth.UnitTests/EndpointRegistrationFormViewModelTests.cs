using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using WebHealth.Application.Registry;
using WebHealth.Web.Models;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class EndpointRegistrationFormViewModelTests
{
    private static readonly Guid ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WebsiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EnvironmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OwnerId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static IReadOnlyList<ValidationResult> Validate(EndpointRegistrationFormViewModel model) =>
        [.. model.Validate(new ValidationContext(model))];

    private static IReadOnlyList<string> Fields(EndpointRegistrationFormViewModel model) =>
        [.. Validate(model).SelectMany(result => result.MemberNames)];

    [Fact]
    public void ExistingEnvironment_DoesNotRequireAnyHierarchyName()
    {
        var model = new EndpointRegistrationFormViewModel
        {
            Url = "https://example.test/",
            ClientId = ClientId,
            WebsiteId = WebsiteId,
            EnvironmentId = EnvironmentId
        };

        model.HierarchyMode.Should().Be(EndpointRegistrationModes.ExistingEnvironment);
        Validate(model).Should().BeEmpty();
    }

    [Fact]
    public void NewClient_WithoutAName_IsRejected()
    {
        var model = new EndpointRegistrationFormViewModel
        {
            Url = "https://example.test/",
            ClientId = EndpointRegistrationFormViewModel.CreateNew,
            ClientOwnerSubjectId = OwnerId,
            WebsiteName = "Website",
            WebsiteOwnerSubjectId = OwnerId,
            EnvironmentName = "Production"
        };

        Validate(model).Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("Enter a name for the new client.");
    }

    [Fact]
    public void NewWebsite_WithoutANameOrEnvironmentName_IsRejected()
    {
        var model = new EndpointRegistrationFormViewModel
        {
            Url = "https://example.test/",
            ClientId = ClientId,
            WebsiteId = EndpointRegistrationFormViewModel.CreateNew,
            WebsiteOwnerSubjectId = OwnerId
        };

        Fields(model).Should().BeEquivalentTo(
            [
                nameof(EndpointRegistrationFormViewModel.WebsiteName),
                nameof(EndpointRegistrationFormViewModel.EnvironmentName)
            ]);
    }

    [Fact]
    public void NewEnvironment_WithoutANameIsRejectedButNeedsNoClientOrWebsiteName()
    {
        var model = new EndpointRegistrationFormViewModel
        {
            Url = "https://example.test/",
            ClientId = ClientId,
            WebsiteId = WebsiteId,
            EnvironmentId = EndpointRegistrationFormViewModel.CreateNew
        };

        Validate(model).Should().ContainSingle()
            .Which.MemberNames.Should().Equal(
                nameof(EndpointRegistrationFormViewModel.EnvironmentName));
    }
}
