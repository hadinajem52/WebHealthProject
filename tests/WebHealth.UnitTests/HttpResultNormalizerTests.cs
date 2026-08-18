using System.Text;
using FluentAssertions;
using WebHealth.Application.Monitoring;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class HttpResultNormalizerTests
{
    private static readonly DateTimeOffset MeasuredAt =
        new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PolicyFingerprint_ChangesForEveryEffectivePolicyField()
    {
        var baseline = PolicyFingerprintInput();
        var baselineFingerprint = HttpPolicyFingerprint.Create(baseline);
        var variants = new[]
        {
            baseline with { NormalizedUrl = "https://other.test/" },
            baseline with { MonitorType = "Other" },
            baseline with { IsProduction = true },
            baseline with { IntervalSeconds = 61 },
            baseline with { TimeoutSeconds = 11 },
            baseline with { FailureConfirmationCount = 3 },
            baseline with { RecoveryConfirmationCount = 3 },
            baseline with { WarningThresholdMs = 101 },
            baseline with { CriticalThresholdMs = 201 },
            baseline with { AcceptedStatusCodes = [204, 404] },
            baseline with { RequiredContentMarker = "READY" },
            baseline with { ContentMarkerComparison = "Ordinal" },
            baseline with { ProductionHttpSeverity = FindingSeverities.Critical },
            baseline with { MaxResponseBodyBytes = 1024 },
            baseline with { MaxRedirects = 4 }
        };

        variants.Should().OnlyContain(variant =>
            HttpPolicyFingerprint.Create(variant) != baselineFingerprint);
    }

    [Fact]
    public void PolicyFingerprint_SortsAndDeduplicatesAcceptedStatuses()
    {
        var first = PolicyFingerprintInput() with { AcceptedStatusCodes = [404, 204, 404] };
        var second = PolicyFingerprintInput() with { AcceptedStatusCodes = [204, 404] };

        HttpPolicyFingerprint.Create(first).Should().Be(HttpPolicyFingerprint.Create(second));
    }

    [Theory]
    [InlineData(200, false, "Healthy", null)]
    [InlineData(204, false, "Healthy", null)]
    [InlineData(404, true, "Healthy", null)]
    [InlineData(404, false, "Critical", "ClientError")]
    [InlineData(500, true, "Critical", "ServerError")]
    public void Normalize_EvaluatesDefaultConfiguredAndServerStatuses(
        int status,
        bool acceptStatus,
        string outcome,
        string? failureCategory)
    {
        var policy = HttpResultPolicy.Default with
        {
            AcceptedStatusCodes = acceptStatus ? [status] : []
        };

        var result = Normalize(Success(status), policy);

        result.Outcome.Should().Be(outcome);
        result.FailureCategory.Should().Be(failureCategory);
    }

    [Fact]
    public void Normalize_EvaluatesRequiredMarkerWithConfiguredCaseRule()
    {
        var insensitive = HttpResultPolicy.Default with
        {
            RequiredContentMarker = "READY",
            IsContentMarkerCaseSensitive = false
        };
        var sensitive = insensitive with { IsContentMarkerCaseSensitive = true };

        Normalize(Success(200, "service ready"), insensitive).Outcome.Should().Be("Healthy");
        var mismatch = Normalize(Success(200, "service ready"), sensitive);

        mismatch.FailureCategory.Should().Be("ContentMismatch");
        mismatch.Findings.Should().ContainSingle(finding => finding.RuleKey == "Http.ContentMismatch");
        mismatch.Findings.Single().ObservedValue.Should().NotContain("service ready");
        mismatch.Findings.Single().ExpectedValue.Should().NotContain("READY");
    }

    [Fact]
    public void Normalize_RequiresProductionHttpTargetsToFinishOnHttps()
    {
        var request = new SafeHttpTransportRequest(Guid.NewGuid(), "http://example.test/", true);
        var transport = Success(200) with
        {
            FinalDestination = new("http://example.test/")
        };

        var result = HttpResultNormalizer.Normalize(new(
            request, transport, HttpResultPolicy.Default, MeasuredAt));

        result.Outcome.Should().Be("Warning");
        result.FailureCategory.Should().Be("HttpsRequired");
    }

    [Fact]
    public void Normalize_AcceptsProductionHttpTargetThatFinishesOnHttps()
    {
        var request = new SafeHttpTransportRequest(Guid.NewGuid(), "http://example.test/", true);
        var transport = Success(200) with
        {
            FinalDestination = new("https://example.test/")
        };

        var result = HttpResultNormalizer.Normalize(new(
            request, transport, HttpResultPolicy.Default, MeasuredAt));

        result.Outcome.Should().Be("Healthy");
        result.Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SafeHttpFailureKind.NameResolution, "Dns", "Critical")]
    [InlineData(SafeHttpFailureKind.Connection, "Connection", "Critical")]
    [InlineData(SafeHttpFailureKind.Tls, "Tls", "Critical")]
    [InlineData(SafeHttpFailureKind.Timeout, "Timeout", "Critical")]
    [InlineData(SafeHttpFailureKind.Cancelled, "Cancellation", "Cancelled")]
    [InlineData(SafeHttpFailureKind.RedirectLoop, "RedirectLoop", "Critical")]
    [InlineData(SafeHttpFailureKind.RedirectLimit, "ExcessiveRedirects", "Critical")]
    public void Normalize_MapsSafeTransportFailures(
        SafeHttpFailureKind transportFailure,
        string failureCategory,
        string outcome)
    {
        var result = Normalize(Failure(transportFailure));

        result.FailureCategory.Should().Be(failureCategory);
        result.Outcome.Should().Be(outcome);
        result.SafeDiagnostic.Should().NotBeNullOrWhiteSpace();
        result.Findings.Should().HaveCount(transportFailure == SafeHttpFailureKind.Cancelled ? 0 : 1);
    }

    [Fact]
    public void Normalize_ClassifiesStreamingLimitWithoutEvaluatingIncompleteMarker()
    {
        var policy = HttpResultPolicy.Default with
        {
            RequiredContentMarker = "expected",
            MaxResponseBodyBytes = 8
        };
        var transport = Success(200, "12345678") with
        {
            BodyTruncated = true,
            ResponseBytesRead = 9
        };

        var result = Normalize(transport, policy);

        result.FailureCategory.Should().Be("ResponseTooLarge");
        result.Findings.Should().ContainSingle(finding => finding.RuleKey == "Http.ResponseTooLarge");
        result.Findings.Should().NotContain(finding => finding.RuleKey == "Http.ContentMismatch");
        result.DecodedLength.Should().Be(9);
        result.LengthSource.Should().Be("BoundedDecoded");
    }

    [Fact]
    public void Normalize_PreservesOrderedQueryFreeRedirectAndLoopEvidence()
    {
        var redirects = new[]
        {
            new SafeHttpRedirectHop(
                302,
                "http://example.test/start",
                "http://example.test/again",
                false),
            new SafeHttpRedirectHop(
                302,
                "http://example.test/again",
                "http://example.test/again",
                true)
        };
        var transport = Failure(SafeHttpFailureKind.RedirectLoop) with { Redirects = redirects };

        var result = Normalize(transport);

        result.Redirects.Select(hop => hop.HopNumber).Should().Equal(1, 2);
        result.Redirects[^1].IsLoop.Should().BeTrue();
        result.Redirects.Should().OnlyContain(hop => !hop.FromUrl.Contains('?') && !hop.ToUrl.Contains('?'));
    }

    [Fact]
    public void Normalize_UsesExplicitPrimaryFailurePrecedence()
    {
        var policy = HttpResultPolicy.Default with { MaxResponseBodyBytes = 8 };
        var transport = Success(500, "12345678") with
        {
            BodyTruncated = true,
            ResponseBytesRead = 9
        };

        var result = Normalize(transport, policy);

        result.Findings.Select(finding => finding.FailureCategory)
            .Should().Contain(["ResponseTooLarge", "ServerError"]);
        result.FailureCategory.Should().Be("ResponseTooLarge");
    }

    [Fact]
    public void Normalize_UsesVersionedIssueKeysIndependentOfSeverity()
    {
        var warningPolicy = HttpResultPolicy.Default with
        {
            ProductionHttpSeverity = FindingSeverities.Warning
        };
        var criticalPolicy = warningPolicy with
        {
            ProductionHttpSeverity = FindingSeverities.Critical
        };
        var request = new SafeHttpTransportRequest(Guid.NewGuid(), "http://example.test/", true);
        var transport = Success(200) with { FinalDestination = new("http://example.test/") };

        var warning = HttpResultNormalizer.Normalize(new(request, transport, warningPolicy, MeasuredAt));
        var critical = HttpResultNormalizer.Normalize(new(request, transport, criticalPolicy, MeasuredAt));

        warning.Findings.Single().IssueKey.Should()
            .Be("v1|HttpAvailability|Http.HttpsRequired|default");
        critical.Findings.Single().IssueKey.Should().Be(warning.Findings.Single().IssueKey);
    }

    private static NormalizedCheckResult Normalize(
        SafeHttpTransportResult transport,
        HttpResultPolicy? policy = null) =>
        HttpResultNormalizer.Normalize(new(
            new(Guid.NewGuid(), "https://example.test/", false),
            transport,
            policy ?? HttpResultPolicy.Default,
            MeasuredAt));

    private static SafeHttpTransportResult Success(int status, string body = "ok") => new(
        null,
        status,
        new("https://example.test/"),
        TimeSpan.FromMilliseconds(123.4),
        Encoding.UTF8.GetByteCount(body),
        false,
        Encoding.UTF8.GetBytes(body),
        []);

    private static SafeHttpTransportResult Failure(SafeHttpFailureKind failure) => new(
        failure,
        null,
        null,
        TimeSpan.FromMilliseconds(25),
        0,
        false,
        ReadOnlyMemory<byte>.Empty,
        []);

    private static HttpPolicyFingerprintInput PolicyFingerprintInput() => new(
        "https://example.test/",
        HttpIssueIdentity.MonitorType,
        false,
        60,
        10,
        2,
        2,
        100,
        200,
        [],
        null,
        "OrdinalIgnoreCase",
        FindingSeverities.Warning,
        SafeHttpTransportDefaults.MaxDecodedBodyBytes,
        SafeHttpTransportDefaults.MaxRedirects);
}
