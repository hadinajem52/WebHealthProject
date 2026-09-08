using FluentAssertions;
using WebHealth.Application.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class ResolvedSslPolicyTests
{
    [Fact]
    public void Fingerprint_ChangesForEveryResolvedPolicyFieldAndTarget()
    {
        var policy = ResolvedSslPolicy.Default;
        var original = policy.Fingerprint("https://example.test/status", false);
        var changes = new[]
        {
            policy with { IntervalSeconds = 43200 },
            policy with { TimeoutSeconds = 20 },
            policy with { FailureConfirmationCount = 2 },
            policy with { RecoveryConfirmationCount = 2 },
            policy with { WarningExpiryDays = 31 },
            policy with { HighExpiryDays = 16 },
            policy with { CriticalExpiryDays = 8 }
        };
        changes.Select(change => change.Fingerprint("https://example.test/status", false))
            .Should().OnlyContain(value => value != original).And.OnlyHaveUniqueItems();
        policy.Fingerprint("https://example.test/status", true).Should().NotBe(original);
        policy.Fingerprint("https://example.test:8443/status", false).Should().NotBe(original);
        policy.Fingerprint("https://other.test/status", false).Should().NotBe(original);
        policy.Fingerprint("https://example.test/status", false).Should().Be(original);
    }

    [Fact]
    public void MatchesFingerprint_AcceptsOnlyComputedCurrentOrDefaultExpiryLegacyHashes()
    {
        var policy = ResolvedSslPolicy.Default;
        const string url = "https://example.test/status";
        var legacy = policy.LegacyFingerprint(url, false);
        policy.MatchesFingerprint(legacy, url, false).Should().BeTrue();
        policy.MatchesFingerprint(policy.Fingerprint(url, false), url, false).Should().BeTrue();
        policy.MatchesFingerprint(legacy, url, true).Should().BeFalse();
        policy.MatchesFingerprint(new string('a', 64), url, false).Should().BeFalse();
        var changed = policy with { WarningExpiryDays = 31 };
        changed.MatchesFingerprint(legacy, url, false).Should().BeFalse();
        changed.MatchesFingerprint(changed.Fingerprint(url, false), url, false).Should().BeTrue();
    }

    [Theory]
    [InlineData(30, 30, 7)]
    [InlineData(30, 15, 15)]
    [InlineData(30, 15, -1)]
    public void Fingerprint_RejectsInvalidExpiryOrder(int warning, int high, int critical)
    {
        var policy = new ResolvedSslPolicy(WarningExpiryDays: warning, HighExpiryDays: high, CriticalExpiryDays: critical);
        var calculate = () => policy.Fingerprint("https://example.test/", false);
        calculate.Should().Throw<ArgumentException>();
    }
}
