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
