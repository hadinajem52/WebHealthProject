using FluentAssertions;
using WebHealth.Application.Incidents;
using WebHealth.Domain.Incidents;
using WebHealth.Web.Models;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class IncidentEvidenceDisplayTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 9, 8, 13, 41, 9, TimeSpan.Zero);

    [Fact]
    public void Proof_PairsTheObservedValueWithWhatWasExpected()
    {
        Display(Evidence(IncidentEvidenceRoles.ConfirmedFailure, Proof(
                observedValue: "Disallow: /",
                expectedValue: "A crawlable site root",
                httpStatus: 200,
                totalDurationMs: 1061)))
            .Should().Be("Disallow: / · expected A crawlable site root · HTTP 200 · 1061 ms");
    }

    [Fact]
    public void Proof_FallsBackToTheSafeDiagnosticWhenTheCheckRecordedNoFinding()
    {
        Display(Evidence(IncidentEvidenceRoles.ConfirmedFailure, Proof(
                safeDiagnostic: "Connection timed out",
                totalDurationMs: 10_000)))
            .Should().Be("Connection timed out · 10000 ms");
    }

    [Fact]
    public void Proof_ReportsTheConfirmationThresholdOnlyForTheOpeningEvidence()
    {
        var proof = Proof(observedValue: "503", failureConfirmationCount: 3);

        Display(Evidence(IncidentEvidenceRoles.ConfirmationThreshold, proof))
            .Should().Be("503 · confirmed by 3 consecutive failures");
        Display(Evidence(IncidentEvidenceRoles.ConfirmedFailure, proof))
            .Should().Be("503");
    }

    [Fact]
    public void Proof_NamesTheActorWhenAPersonRecordedTheEvidence()
    {
        Display(Evidence(IncidentEvidenceRoles.ResolveManually, proof: null, actorDisplayName: "Administrator"))
            .Should().Be("Recorded by Administrator");
    }

    [Fact]
    public void Proof_SaysNothingWhenNoCheckFactsSurvive()
    {
        Display(Evidence(IncidentEvidenceRoles.ConfirmedFailure, proof: null)).Should().Be("—");
        Display(Evidence(IncidentEvidenceRoles.ConfirmedFailure, Proof())).Should().Be("—");
    }

    private static string Display(IncidentEvidenceItem evidence) =>
        IncidentEvidenceDisplay.Proof(evidence);

    private static IncidentEvidenceItem Evidence(
        string evidenceRole,
        IncidentEvidenceProof? proof,
        string? actorDisplayName = null) =>
        new(Guid.NewGuid(),
            IncidentEvidenceTypes.Failure,
            evidenceRole,
            CapturedAt,
            proof is null ? null : Guid.NewGuid(),
            actorDisplayName,
            proof);

    private static IncidentEvidenceProof Proof(
        string? failureCategory = null,
        int? httpStatus = null,
        int? totalDurationMs = null,
        string? safeDiagnostic = null,
        string? observedValue = null,
        string? expectedValue = null,
        int? failureConfirmationCount = null) =>
        new(failureCategory,
            httpStatus,
            totalDurationMs,
            safeDiagnostic,
            observedValue,
            expectedValue,
            failureConfirmationCount);
}
