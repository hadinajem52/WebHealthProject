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
                expectedValue: "A crawlable site root")))
            .Should().Be("Disallow: / · expected A crawlable site root");
    }

    [Fact]
    public void Proof_LeavesOutCheckFactsThatBelongToAnotherIssue()
    {
        Display(Evidence(IncidentEvidenceRoles.ConfirmedFailure, Proof(
                observedValue: "Disallow: /",
                expectedValue: "A crawlable site root",
                safeDiagnostic: "Response was slower than the configured threshold.")))
            .Should().Be("Disallow: / · expected A crawlable site root");
    }

    [Fact]
    public void Proof_FallsBackToTheSafeDiagnosticWhenTheCheckRecordedNoFinding()
    {
        Display(Evidence(IncidentEvidenceRoles.ConfirmedFailure, Proof(
                safeDiagnostic: "Connection timed out")))
            .Should().Be("Connection timed out");
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
        string? severity = null,
        string? observedValue = null,
        string? expectedValue = null,
        string? safeDiagnostic = null,
        int? failureConfirmationCount = null) =>
        new(severity,
            observedValue,
            expectedValue,
            safeDiagnostic,
            failureConfirmationCount);
}
