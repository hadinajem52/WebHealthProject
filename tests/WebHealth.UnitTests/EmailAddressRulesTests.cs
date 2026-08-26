using FluentAssertions;
using WebHealth.Domain.Normalization;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class EmailAddressRulesTests
{
    [Theory]
    [InlineData("alerts@example.com")]
    [InlineData("first.last@example.co.uk")]
    [InlineData("ops+monitoring@example.com")]
    [InlineData("a@example.com")]
    [InlineData("alerts@bücher.example")]
    public void IsValid_AcceptsDeliverableAddresses(string candidate) =>
        EmailAddressRules.IsValid(candidate).Should().BeTrue(candidate);

    [Theory]
    [InlineData("name@@example.com")]
    [InlineData("a b@example.com")]
    [InlineData("..x..@example.com")]
    [InlineData(".leading@example.com")]
    [InlineData("trailing.@example.com")]
    [InlineData("double..dot@example.com")]
    public void IsValid_RejectsAddressesRecipientNormalizerLetsThrough(string candidate)
    {
        EmailAddressRules.IsValid(candidate).Should().BeFalse(candidate);
        RecipientNormalizer.Normalize(candidate).Should().NotBeNull(
            "this test only earns its keep while the normalizer still accepts the address");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@example.com")]
    [InlineData("alerts@")]
    [InlineData("alerts")]
    [InlineData("alerts@localhost")]
    [InlineData("alerts@-example.com")]
    [InlineData("alerts@example-.com")]
    [InlineData("alerts@exa mple.com")]
    public void IsValid_RejectsMalformedAddresses(string? candidate) =>
        EmailAddressRules.IsValid(candidate).Should().BeFalse(candidate ?? "<null>");

    [Fact]
    public void IsValid_RejectsAddressesBeyondTheStorageLimit()
    {
        var localPart = new string('a', 64);
        var oversized = $"{localPart}@{new string('b', 255)}.com";

        oversized.Length.Should().BeGreaterThan(EmailAddressRules.MaxAddressLength);
        EmailAddressRules.IsValid(oversized).Should().BeFalse();
    }

    [Fact]
    public void IsValid_RejectsLocalPartsBeyondSixtyFourCharacters() =>
        EmailAddressRules.IsValid($"{new string('a', 65)}@example.com").Should().BeFalse();

    [Fact]
    public void IsValid_AcceptsLocalPartsAtSixtyFourCharacters() =>
        EmailAddressRules.IsValid($"{new string('a', 64)}@example.com").Should().BeTrue();
}
