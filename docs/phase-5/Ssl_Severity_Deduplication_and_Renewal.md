# SSL Severity, Deduplication, and Renewal

**Work item:** Phase 5 / increment 5.3
**Rules:** BR-C04, BR-C05, BR-C06, BR-I04
**Acceptance criteria contribution:** AC-06 severity. Observation and scheduling were delivered in increment 5.2.

## The boundary decision, written down first

`CertificateExpiry.SelectSeverity` is the single place the BR-C04 bands are decided, and it was written before any UI or persistence touched them. AC-06 tests 30, 15 and 7 alongside 31, 16 and 8, so the comparison direction had to be a decision rather than a side effect of whichever operator got typed.

**Every boundary is inclusive on the unhealthy side.** Exactly 30 days remaining is already a warning, exactly 15 is already high, exactly 7 is already critical; 31, 16 and 8 each sit one band lower.

| Days remaining | Band |
|---|---|
| 31 or more | none |
| 30 – 16 | Warning |
| 15 – 8 | High |
| 7 or fewer, including negative | Critical |

An expired certificate reports a negative day count and therefore lands in the critical band by the same comparison. There is no separate expired case in the band logic, so there is no way for a very negative count to fall out of the bands.

The same direction is used for the performance thresholds in increment 5.4. One written rule across both families means a boundary test never has to guess which way a given rule leans.

## Severity is three bands; endpoint health stays three states

`High` is a new finding and incident severity. It is not a new *endpoint health* state.

A certificate that expires in fifteen days is urgent, but the site is serving traffic normally. Endpoint health is the availability signal the dashboard reads (BR-U06), so `High` maps onto a `Warning` endpoint status and a `Warning` result outcome, while the finding and its incident carry the escalated severity. Only a `Critical` finding produces a `Critical` outcome.

This required `EndpointHealth` to be able to hold `Warning` at all. Before this increment the confirmation engine only ever produced `Healthy` or `Critical`, so a confirmed warning silently reported the endpoint as down. The engine now derives the confirmed status from the severity of the issues that confirmed, and treats `Warning` and `Critical` alike as states to recover from.

`FindingSeverities` is an alias of `IncidentSeverities` rather than a parallel set. A finding's severity is written straight onto the incident it confirms, so a second vocabulary would eventually let a rule raise a severity no incident could carry.

## Deduplication needed no new constraint (BR-C05)

The expiry issue key is `v1|SslCertificate|Ssl.Expiry|{sha256-fingerprint}`.

Putting the fingerprint in the discriminator means the **existing** unique index on active `(endpoint_monitor_id, issue_key)` incidents already enforces BR-C05: repeated daily checks of the same certificate reuse one incident, and there is nothing new to migrate.

Approaching expiry and reaching it are **one rule**, sharing one rule key and therefore one issue key. The failure category still separates them — `SslExpiringSoon` while the certificate works, `SslExpired` once it does not. Splitting the key at the expiry date instead would have been the obvious mistake: a certificate crossing its own `notAfter` has not developed a second problem, and a second key would open a duplicate incident for the same certificate and leave the first one unrecognisable at renewal.

The other validation categories — not-yet-valid, hostname mismatch, untrusted — keep their own category-named keys and the default discriminator. They are not expiry, and BR-C05/BR-C06 are scoped to expiry. A certificate that is invalid for one of those reasons gets that finding and no expiry band: stacking a second finding would track one certificate as two issues.

## Renewal resolves the previous certificate's incident (BR-C06)

A renewed certificate has a new fingerprint, so it produces a new issue key, which leaves the previous key with nothing left to observe. `SslMonitorIdentity.IsSupersededExpiryIssueKey` is how that is recognised, and it is deliberately narrow: it matches expiry keys for some *other* certificate, so it cannot sweep up a hostname-mismatch incident on the way past.

On a superseded incident the pipeline records a `CertificateRenewed` timeline event and resolves the incident with resolution category `CertificateRenewed`, distinct from `AutomaticRecovery` — the same subject did not start passing again; the subject was replaced.

Two ordering decisions:

- **The event is written only once the resolution is going to happen.** The lifecycle transition is evaluated first, so an incident can never be left carrying a renewal event it did not act on.
- **Resolution does not wait for a wholly healthy result.** A certificate renewed into another warning band never produces one, and the stale incident would stay open against a certificate that no longer exists. The observation of the replacement *is* the confirmation, which is sound only because a certificate monitor confirms in a single observation by design (increment 5.2): unlike a flapping HTTP response, the certificate a host presents does not alternate between checks.

BR-I04 holds without any extra work: the expiry issue key and the availability issue keys are different keys on different monitors, so a certificate expiry incident and an HTTP outage incident are tracked independently.

## Days remaining are counted from one instant

Days remaining are counted from the result's `MeasuredAt`, and the value stored on the certificate observation is computed from the same instant. A stored day count can therefore never disagree with the severity that was raised from it, and the endpoint page re-derives the band from the stored count rather than from the current clock — so the page and the check that produced it always show the same severity.

## Migration

`SslSeverityAndPerformanceRules` widens check constraints only; it adds no columns and no data.

- `ck_finding_severity` and `ck_incident_severity` gain `High`.
- `ck_check_result_failure_category` gains `SslExpiringSoon`.
- `ck_incident_event_type` and `ck_incident_event_fields` gain `CertificateRenewed`, shaped like `EvidenceRecorded`: no status change, a bounded note required.

## Verification evidence

Pure unit tests, which is the whole point of putting the boundary in a domain function first:

- `CertificateExpiryTests.SelectSeverity_TreatsEveryBoundaryDayAsInsideItsBand` — 30/15/7 and 31/16/8 plus `int.MaxValue`, straight at the domain function.
- `SelectSeverity_TreatsAnAlreadyExpiredCertificateAsCritical`, `SelectSeverity_HonoursOverriddenThresholds`, `SelectSeverity_RejectsUnorderedThresholds`.
- `SslResultNormalizerTests.Normalize_RaisesTheExpiryBandForAValidCertificate` — the same boundaries through the full normalizer, so the wiring is pinned and not only the arithmetic.
- `Normalize_KeepsAnExpiringCertificateOutOfCriticalUntilTheCriticalBand` — the High-to-Warning-outcome decision.
- `Normalize_KeysTheExpiryIssueByFingerprint` — repeated checks share a key, a different certificate does not.
- `Normalize_ReportsAnExpiredCertificateOnTheSameIssueKeyItUsedWhileValid` — the shared-key decision above.
- `IsSupersededExpiryIssueKey_RecognisesOnlyExpiryKeysForOtherCertificates` and `..._RecognisesAnExpiredCertificatesIssueKey` — BR-C06 detection, including that it leaves non-expiry keys alone.
- `HealthConfirmationEngineTests` — a confirmed warning issue confirms `Warning` rather than `Critical`, `High` also confirms `Warning`, a critical issue escalates an already-confirmed warning, and a confirmed warning recovers like a confirmed critical.

## Known limitation

Renewal is detected from the fingerprint of the observation itself, so a renewal that never had an open expiry incident produces no `CertificateRenewed` event. The certificate observation history still records every fingerprint, so the renewal remains visible; it is simply not an incident timeline entry, because there is no incident timeline to write it to.
