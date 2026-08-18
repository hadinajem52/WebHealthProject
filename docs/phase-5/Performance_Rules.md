# Performance Rules

**Work item:** Phase 5 / increment 5.4
**Rules:** BR-P01, BR-P02, BR-P03, BR-P04, BR-P05, BR-I04
**Depends on:** the severity vocabulary and the confirmation engine changes described in [increment 5.3](Ssl_Severity_Deduplication_and_Renewal.md).

## One threshold direction across the phase

`PerformanceEvaluation.SelectResponseTimeSeverity` and `SelectPageSizeSeverity` are pure domain functions, written before anything called them, for the same reason the certificate bands were: a boundary that is decided by whichever operator got typed is a boundary nobody can test against.

**Thresholds are inclusive on the unhealthy side.** A 1,500 ms budget states the response should stay *under* 1,500 ms, so exactly 1,500 ms has already missed it. Exactly 3,000 ms is critical. A page of exactly 2 MB warns.

That is the same direction the certificate bands use — 30 days remaining is already a warning — so a boundary test never has to guess which way a given rule leans.

| Total response time | Band |
|---|---|
| under 1,500 ms | none |
| 1,500 – 2,999 ms | Warning |
| 3,000 ms and above | Critical |

## Thresholds come from the check's own snapshot (BR-P02)

`CreatePolicy` reads `WarningThresholdMs` and `CriticalThresholdMs` from the logical check's `check_configuration_snapshot`, never from the monitor as it stands now. Re-reading a stored result therefore judges it against the thresholds that were in force when it was measured, not against whatever they were changed to afterwards. A snapshot that recorded no value falls back to the documented defaults.

### Endpoint overrides are now reachable

BR-P02 allows endpoint overrides, and until this increment the monitor stored threshold columns that no command, form or validator could set — the override was consumable but unreachable. `ResponseThresholdOverride.Decide` now sits on the create and update paths:

- submitting neither value means "use the documented 1,500 / 3,000 ms defaults";
- submitting one without the other is **rejected rather than half-applied**, because a warning threshold above an unchanged critical one produces a band nothing can ever fall into;
- critical must be at or above warning, and both must sit between 1 ms and 300 s;
- equal values are allowed — collapsing the warning band is a deliberate "treat any breach as critical" choice, not a mistake.

The endpoint update path already recomputed the monitor's configuration fingerprint from its own columns, so an override flows into the fingerprint, into the next snapshot, and into the results judged under it without any new plumbing. The endpoint page states the effective values and whether they are an override or the default.

### The 1,000 ms monitors that already exist

Monitors created before this increment carry `WarningThresholdMs = 1000`, a value that predates BR-P02's stated 1,500 ms default and that no operator chose. `CreateMonitor` now seeds `ResponseTimeThresholds.Default`, so new endpoints get 1,500 / 3,000.

**Existing rows are deliberately not backfilled.** 1,000 ms is a real, stored, historical threshold: every result already measured under it was judged against it, and the fingerprint in each of those snapshots records that. Rewriting the column now would make stored history disagree with the thresholds its own snapshots claim. The override surface above is the remedy — an operator can move an existing endpoint to any value they want, and the fingerprint follows.

## Slow response is a separate issue from availability (BR-I04)

`PerformanceRules.SlowResponse` is its own rule key, so `HttpIssueIdentity.Create` produces its own issue key, so it gets its own `IssueState` row and its own incident. A server error that also took four seconds is two facts, and they track as two issues.

The finding is raised for every exchange that **produced a response**, including one that failed a rule. An exchange that produced no response is excluded before the rule runs: a timeout's duration is its budget, not a measured response time, and admitting it would drag P95 toward the timeout setting instead of describing how the site performs — precisely what BR-U05 keeps timeouts out of the percentiles for.

### Three consecutive breaches (BR-P03)

The confirmation count is per issue, not per monitor. `ObservedIssue` carries its own `FailureConfirmationCount`, and `PerformanceRules.SelectFailureConfirmationCount` supplies it:

```
slow response  -> max(monitor's count, 3)
everything else -> the monitor's count
```

`max` rather than a flat 3: an operator who configured five confirmations for availability did not ask for three. On a monitor confirming availability in two, a result that is both failing and slow advances the availability issue to confirmed on the second sample and the slow-response issue on the third.

A sample where the slow-response finding is absent resets that counter to zero while leaving the others alone, so one isolated slow response never opens anything.

The engine reports which issues confirmed (`ConfirmedIssueKeys`) rather than each caller re-deriving it, so the threshold lives in exactly one place instead of two that can drift.

### Performance issues must not hold availability incidents open

Making performance findings part of the result outcome created a trap worth writing down: a page-size warning on every sample means the endpoint never produces a wholly healthy result, so under the previous all-or-nothing recovery model an availability incident could never resolve.

Recovery is therefore now counted **per issue**. An issue this result did not observe is passing, even when some other issue on the same endpoint failed, so its recovery counter advances and `RecoveredIssueKeys` lets its incident resolve on its own evidence. The endpoint's overall status still reflects whatever is currently confirmed, so an endpoint with a lingering page-size warning reads as Warning while its availability incident closes.

A healthy endpoint accumulates no recovery credit — there is nothing to recover from — so a failing sample cannot hand unrelated issues a head start toward resolution.

## Page size (BR-P04)

The transport now captures the response's advertised `Content-Length` into `SafeHttpTransportResult.TransferredLength`. A negative or absent value is discarded rather than stored: the header is attacker-controlled input like any other.

Both lengths are stored. `transferred_length` is what the response advertised; `decoded_length` is what was actually read. `length_source` records which one the rule judged:

| Source | Meaning |
|---|---|
| `TransferredContentLength` | the response advertised a length — bytes as transferred, compression included |
| `MeasuredDecoded` | no length advertised, so decoded bytes actually read |
| `BoundedDecoded` | the body hit the read cap and nothing was advertised: a lower bound, not a measurement |

The transferred length is preferred wherever it exists, because that is what a visitor downloads — **including when the body was truncated**. An advertised 3 MB is exact whether or not the read stopped at 2 MB, and an oversized page is exactly what it describes; only a truncated body that advertised nothing is left unjudged, and `ResponseTooLarge` already reports that one.

Page size is a warning-only rule. A large page is a quality problem, never a reason to call an endpoint down.

## Comparability (BR-P05)

`PerformanceComparability.Evaluate` takes each sample's monitor source and configuration fingerprint and reports whether the set may be read against itself, plus why not when it may not. A certificate probe's duration and an HTTP check's duration are not the same quantity, and neither are two HTTP checks whose timeout or redirect budget changed between them.

The samples are kept and the mixture is stated, rather than the mismatched ones being dropped: a set with a stated gap is more useful than a set that quietly omitted half its history.

The check-history page shows each result's monitor source and renders the warning above the table — before the numbers are read, not after. The same function is reused by the reporting summary in [increment 5.5](Shared_Reporting_Query_Core.md), so "comparable" has one definition across the application.

## Migration

`SslSeverityAndPerformanceRules` widens `ck_check_result_failure_category` to admit `SlowResponse` and `PageTooLarge`. No columns are added: `transferred_length`, `decoded_length` and `length_source` have existed since `HttpMonitoringHistory` and were simply never populated.

## Verification evidence

Pure unit tests, which is where nearly all of this rule family lives:

- `PerformanceRuleTests.ResponseTimeSeverity_TreatsTheThresholdItselfAsABreach` — 1,499 / 1,500 / 2,999 / 3,000 at the domain function.
- `ResponseTimeSeverity_HonoursEndpointOverrides` and `PageSizeSeverity_WarnsAtOrAboveTheThreshold`.
- `SlowResponse_IsRaisedAsItsOwnIssueSeparateFromAvailability` — BR-I04: a slow 500 produces two findings with two issue keys.
- `SlowResponse_UsesTheThresholdsSnapshottedWithTheCheck` — the same duration reads differently under a different snapshot.
- `SlowResponse_NeedsThreeConsecutiveBreachesBeforeItCanConfirm` — including that a stricter monitor count wins.
- `PageSize_PrefersTheTransferredLengthAndLabelsIt`, `PageSize_FallsBackToTheDecodedCountWhenNoLengthWasAdvertised`, `PageSize_IsNotJudgedFromATruncatedBodyThatAdvertisedNoLength`, `PageSize_IsJudgedFromATruncatedBodyThatDidAdvertiseALength`.
- `PageSize_IsNotMeasuredForAFailedExchange` — BR-P01 missing-value handling: no page is not a page of zero bytes, and no slow-response finding is raised either.
- `Comparability_*` — one source and one configuration is comparable; a mixed source, a changed configuration, and both at once each produce their own warning.
- `CheckResultIssuesTests` — the glue the engine and the incident automation both read: healthy and cancelled results observe nothing, one result carries two issues with two different confirmation counts, several findings on one key collapse to the most severe, and a failure with no finding still observes something to count.
- `ResponseThresholdOverrideTests` — the override rule, including that half an override is rejected and that equal thresholds are allowed.
- `HealthConfirmationEngineTests.SlowResponse_ResetsOnASampleThatIsNotSlow`, `..._ResetsOnAPassingSample`, `AnIssueRecoversWhileAnotherIssueOnTheSameEndpointKeepsFailing`, `ARecoveringIssue_RestartsItsRecoveryCountWhenItFailsAgain`, `AHealthyEndpointDoesNotAccumulateRecoveryCredit`.

## Known limitations

- The page-size threshold is a constant (2 MB), not an endpoint override. BR-P04 states a default and says nothing about overrides, so none was built.
- BR-P05's comparability warning is surfaced on the check-history page and in the reporting summary. Whether that is the right *place* for it on the dashboard is settled in increment 5.6.
