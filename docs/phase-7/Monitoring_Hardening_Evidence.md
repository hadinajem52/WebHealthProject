# Monitoring hardening evidence

## P7-MON-01 — immediate correctness

Implements BR-Q01, BR-Q02, BR-Q04, BR-Q07 and monitoring lifecycle/type safeguards;
preserves AC-02, AC-06, AC-10 and AC-15 behavior.

The shared connector validates the raw DNS answer count before normalization and deduplication,
rejects the whole destination if any answer is prohibited, and attempts permitted addresses in
resolver order. Each attempt owns a fresh socket and a per-IP concurrency slot. The address
wait/connect timeout defaults to five seconds, bounded to one through ten seconds. Caller and
whole-request cancellation remain absolute deadlines. A failed address advances to the next;
peer-policy rejection fails closed. Host and global leases continue to cover the whole request.
Only identifiers and safe failure categories enter monitoring logs; no new target or content
logging was introduced.

`EndpointMonitorReconciler` owns HTTP/SSL creation and reconciliation. Endpoint archive retires
only active rows with the same actor and timestamp as the endpoint, without changing cadence,
due time, scheduling mode, or pause choice. Restore considers only that archive operation,
ordered by creation descending and ID ascending, and keeps PageAudit retired. Registry edits
cannot change an archived target, so matching archive retirement also preserves target identity.
SSL host/port changes retire the active SSL identity and create its replacement through the
DbSet. Restoring an endpoint leaves the endpoint disabled. Current-truth generation is added
in P7-MON-02 with the snapshot schema change.

HTTP policy updates now switch explicitly by monitor type. SSL retains its dedicated update;
PageAudit stays in its own configuration path; unknown types throw within the transaction.
Execution likewise selects HTTP or SSL explicitly and rejects other types before transport.
Existing server-side role and assignment checks remain in place.

No schema migration or compiled-model update is needed for this increment. The new timeout
setting and bounds are documented in `setup/README.md`.

## Verification

Recorded 2026-09-08 on the local Windows workspace, .NET 10.0.400 SDK / 10.0.11 runtime:

| Command | Result |
| --- | --- |
| `dotnet test tests/WebHealth.UnitTests --verbosity quiet` | 738 passed |
| `dotnet test tests/WebHealth.IntegrationTests --filter 'FullyQualifiedName!~DatabaseFoundationTests' --verbosity quiet` | 621 passed, 3 existing skips |
| `scripts/run-database-foundation-tests.ps1` | Full ordered suite passed, explicit migration database updated; Release build had zero warnings/errors |
| `git diff --check` | Passed |

The database regression owns a registered endpoint and verifies historical retirement metadata,
manual-only and paused state, cadence, active monitor identity, PageAudit non-restoration, and
HTTP policy isolation from SSL/PageAudit. Existing database stages exercise SSL replacement
on host/port/scheme changes. Transport coverage includes IPv6 failure followed by mapped IPv4
success, raw-answer limits before deduplication, mixed prohibited DNS answers, per-address
queue timeout fallback, caller cancellation, slot reacquisition, redirects, TLS validation, and
whole-request deadlines. Direct authorization tests pass in the ordinary integration suite.

The broader run exposed a pre-existing badge-test parser defect: its regex failed on label spans
with `data-live` attributes. The test now parses the DOM and checks each badge's label text;
no badge rendering was changed.

This is local personal-project evidence, not deployment certification. Later increments remain
required for snapshot immutability, authorization revocation, SSL hardening, diagnostics,
retention, and representative load/recovery evidence.

## P7-MON-02 — implemented

Snapshot v2 freezes target URL, host, port, normalization, production classification, policy,
and monitor generation. Scheduled, manual, and urgent creation share the builder. HTTP/SSL
execution and evidence validation use the recorded target; v1 compatibility is explicit.
PostgreSQL triggers advance generations atomically for policy and lifecycle mutations, including
set-based parent cascades. Cadence updates alone do not advance generation. Finalization locks
and refreshes current state; superseded/ineligible history cannot change health, issue counters,
incidents, notifications, or urgent work.

Current target authorization is checked immediately before every socket, including HTTP redirects
and DNS fallback. Administrator/Operations can grant or revoke endpoint/host/port evidence through
endpoint Actions. Existing endpoints receive no fabricated permission. Evidence/reasons are absent
from snapshots, logs, and audit payloads; grant/revoke audits record identifiers and action only.

Verification on 2026-09-08:

| Check | Result |
| --- | --- |
| Unit suite | 738 passed |
| Ordinary integration suite | 634 passed, three existing skips |
| Full ordered database foundation script | Passed, including explicit migration application |
| EF pending-model check | No model drift |
| Browser against disposable PostgreSQL fixtures | Endpoint superseded message visible; permission empty state, grant to Active, revoke to Revoked verified |
| Visual inspection | Dashboard fonts/cards retained; corrected an empty validation-summary box found during inspection |

Database regressions cover pause/resume, scheduling toggle, endpoint disable/enable, archive/restore,
policy changes, all parent lifecycle toggles, HTTP scheme changes, SSL host/port/scheme replacement,
and recorded-target execution after edits. Old results persist without current health/issues/incidents.
Transport tests prove revoked permission causes zero socket contacts and fallback rechecks permission.
Direct service and MVC tests cover roles, anti-forgery, duplicate grants, idempotent revoke, audit privacy,
identity matching, and expiry boundaries. Snapshot tests cover legacy resolution, missing/conflicting
v2 fields, unsupported versions, and database rejection of invalid snapshots.

An isolated populated database regression found and now protects a rollback defect. Rollback cancels
unfinished v2 work, clears leases, preserves completed result facts, and converts snapshots to the
legacy representation before dropping target fields. It flushes deferred constraints before schema
changes. Reapplication is repeatable. This explicit rollback loses v2-only target/disposition facts
and permission grants; the setup guide documents that limitation. Normal upgrades preserve v1 history.

The migrations have only been applied to disposable verification databases, not the user's application
database. The missing Detail_Page_UI_Pattern.md reference was checked; existing endpoint cards and
fact rows supplied the UI reference. Mobile visual checks and broader changed-UI evidence remain part
of the final release gate. Increments 3–7, including AC-14/AC-15, remain required.

## P7-MON-03 — implemented

Typed v2 HTTP overrides share one policy resolver across registry updates and queued snapshots.
New and reset monitors use a 15-second timeout. The data migration preserves existing materialized
values, including 30-second timeouts, as explicit overrides. Status codes are bounded and canonical;
thresholds, confirmation counts, marker length, and comparison modes are validated before saving.
Configuration drift prevents snapshot creation and reports identifiers without marker text.
Scheduled and manual checks capture equivalent resolved policy, with each setting's source.

Endpoint forms expose overrides and clearing them restores defaults. Detail rows show effective
values and sources. Audit facts expose marker presence only. Marker matching decodes the bounded
buffer as UTF-8, US-ASCII, or ISO-8859-1, falling back to UTF-8 for unsupported or malformed charsets.
No response content is reread or persisted.

Verification on 2026-09-08:

| Check | Result |
| --- | --- |
| Unit suite | 767 passed |
| Ordinary integration suite | 634 passed, three existing skips |
| Additional legacy JSON compatibility tests | 3 passed |
| Full ordered database foundation script | Passed; Release build had no warnings or errors |
| EF pending-model check | No model drift; migration changes data only |
| Browser validation | Rejected status 500 without saving; accepted and canonicalized 404, 301, 404 |
| Browser edit and reset | Saved 30-second timeout, three failures, and literal marker; reloaded values; clearing restored 15 seconds, two failures, and no marker/status overrides |
| Visual inspection | Desktop detail cards and 390-pixel mobile layout retained dashboard styling; marker HTML displayed as text |

Database evidence includes named policy fixtures, atomic invalid-update rejection, drift rejection
without queued work, safe audit payloads, scheduled/manual snapshot equivalence, marker/status
execution, and a 15-second reset. An isolated migration database proves 30-second preservation,
repeatable upgrade, populated rollback, and retention of historical snapshot marker facts.
Existing transport and authorization suites protect redirect security, 2xx/5xx behavior, bounded
bodies, role restrictions, and anti-forgery. Setup documents rollback loss of current v2-only fields.

Browser verification used the disposable foundation database with workers and email disabled.
One deliberately orphaned endpoint from a negative database fixture was archived only in that
preview database so the registry editor could list valid fixtures. The browser tool's empty-fill
operation did not clear controls; keyboard selection and Backspace verified the actual reset flow.
No user application database migration was applied. Increments 4–7 remain required.

## P7-MON-04 � in progress

The first slice adds a shared offline TLS policy to the normal HTTP handler and inspection probe:
certificate downloads are disabled, revocation is not checked, and certificate verification flags
remain strict. Normal HTTPS still has no certificate-validation override. The probe rejects its
handshake after inspection and sends no application data.

A certificate with local AIA issuer, OCSP, and CRL URLs and a deliberately unavailable issuer is
served by an OpenSSL loopback fixture. Both client paths are checked against a separate listening
socket for zero certificate-controlled connections. The fixture uses OpenSSL because the Windows
TLS test server made its own OCSP requests even with managed offline settings. This test also
exposed chain-wide PartialChain errors being missed by the probe's per-element trust evaluation;
chain-wide errors now participate in trust evaluation. Resolved SSL policy, structured persistence,
simultaneous findings, and the remaining increment gates are still pending.

Verification on 2026-09-08: 59 transport, SSL probe, and chain-trust integration tests passed.

The next slice evaluates leaf time validity, hostname matching, chain trust, and expiry bands
independently. Several findings can be persisted for one certificate. The display category follows
NotYetValid, Expired, HostnameMismatch, Untrusted, ExpiringSoon precedence. Rule keys use
`Ssl.NotYetValid`, `Ssl.HostnameMismatch`, and `Ssl.Untrusted`; their issue identities retain the
existing category-based encoding so existing incident identity and historical references remain
stable. Expiry keeps its fingerprint-specific issue key. Threshold validation now requires strict
warning > high > critical >= 0 ordering. The unit suite passed 771 tests on 2026-09-08.
The full ordered database foundation script passed, including persistence of three simultaneous SSL findings and issue states. The Release build completed with zero warnings and errors.

An increment-3 follow-up found that the database still rejected equal HTTP warning/critical
thresholds despite the resolver accepting this documented boundary. `HttpThresholdEquality`
relaxes both monitor and snapshot checks. The existing policy workflow now saves equal thresholds
and queues matching snapshots; the populated upgrade/rollback fixture also uses equal thresholds.
Rollback retains the relaxed checks to preserve those policies and immutable historical values.
Verification: full ordered database suite and explicit migrations passed on 2026-09-08; Release build had zero warnings/errors; EF reported no pending model changes. Compiled-model regeneration produced no structural changes.

The SSL probe now captures canonical chain-status names from both element and chain-wide flags: NoError is removed, flags are expanded, names are deduplicated and ordinal-sorted, and output is bounded to 32 names. The observation carries these facts without certificate-controlled strings or encoded certificate bytes. Persistence and UI wiring remain pending. Verification: 60 transport, probe, and chain-trust integration tests passed, including PartialChain evidence from the local incomplete-chain handshake.

Structured certificate persistence is now implemented with separate leaf validity, hostname,
chain trust, and JSON chain-status fields. The endpoint reader and detail view expose these facts
and the revocation limitation. Historical positive trust is conservatively backfilled as Unknown;
negative trust and recorded hostname matches remain available, and validity is derived from the
observation time. The migration does not invent historical status codes. Setup documents explicit
application and rollback loss. Browser verification, stronger schema/backfill checks, and SSL
policy snapshots remain pending before the increment can be marked complete.
Verification on 2026-09-08: full ordered database suite and explicit migrations passed; the new simultaneous-fault fixture verifies all four persisted fields. Release build passed without warnings/errors, compiled model was regenerated, and EF reported no model drift.

ResolvedSslPolicy now defines the daily cadence, 15-second timeout, one-check confirmations, and strictly ordered 30/15/7 expiry defaults. New SSL monitor construction consumes its effective timing/counts. Its SSL-specific canonical fingerprint includes all policy fields, URL, and production classification; four focused tests passed and the infrastructure build passed without warnings/errors. Fingerprint migration and snapshot threshold wiring remain pending, so existing fingerprint storage is unchanged in this slice.

SSL expiry thresholds are now copied into v2 snapshots and consumed by finalization. The database
requires all three values for SSL v2 and enforces strict ordering; HTTP snapshots leave them null.
The migration backfills earlier v2 SSL snapshots with their previously effective 30/15/7 defaults.
V1 compatibility is explicit. SSL-specific fingerprint adoption and remaining evidence gates are
still pending.
Verification on 2026-09-08: full ordered database suite passed, including a 20-day certificate remaining healthy under its snapshotted 10-day warning threshold. Explicit migrations, zero-warning Release build, compiled-model regeneration, and EF no-model-drift check passed.

SSL-specific fingerprints are now used for new monitors and registry updates. Finalization accepts
a canonical hash or an exact computed legacy hash, with legacy compatibility limited to the original
expiry defaults. It requires agreement between logical check and snapshot hashes. The data migration
converts only known hashes for active SSL monitors, advances generation, and preserves queued and
completed historical checks. Its reverse operation recognizes matching default-expiry canonical
hashes rather than rewriting unknown policies. Five focused policy tests passed.
Verification on 2026-09-08: 776 unit tests passed; ordinary Release integration suite passed 640 tests with four opt-in skips (database foundation, Docker, reporting baseline, and live SMTP). The database foundation script passed separately, including populated fingerprint rollback/upgrade, repeatability, and completion of preserved legacy queued work as Superseded. Release build had zero warnings/errors and EF reported no model drift.

A display regression found that the endpoint certificate card used default expiry thresholds and
could select superseded observations. The reader now limits its current certificate to results
accepted as Current and calculates expiry severity from that observation's immutable snapshot.
Expiry severity remains independent of hostname/trust faults. The database regression exercises
a current renewal with a non-default warning threshold followed by a later superseded expired
observation, ensuring the card retains the renewal and its recorded policy.
Verification on 2026-09-08: the full ordered database suite passed with the reader regression; Release build had zero warnings/errors and explicit migrations completed successfully.
