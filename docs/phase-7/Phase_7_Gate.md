# Phase 7 gate — monitoring hardening and release evidence

Phase 7 completed on 2026-09-09 for the personal internship and portfolio project. This gate records local controlled evidence and does not certify a production deployment.

## Representative workload

The opt-in harness created 500 controlled `.test` endpoints, including 350 HTTPS endpoints, 500 HTTP monitors and 350 SSL monitors. Exactly 500 HTTP monitors were due across a distributed 90-second cadence window. All 500 target permissions were active before dispatch. The per-endpoint target permission gate was removed after this gate was recorded; the harness no longer grants permissions, and monitoring no longer requires them.

[Representative monitoring evidence](Representative_Monitoring_Evidence.md) records the machine, PostgreSQL and .NET versions, fixture, dispatch and recovery timings, two 15-minute memory windows and the database outage restart. The repeatable command is `scripts/run-representative-monitoring-evidence.ps1`.

| Performance gate | Measured result | Outcome |
|---|---:|---|
| Creation lag p95 | 84.0 s | Passed, limit 90 s |
| Creation lag maximum | 89.0 s | Passed, limit 5 min |
| Normal queue age p95 | 0.0 s | Passed, limit 2 min |
| Normal queue age maximum | 0.0 s | Passed, limit 10 min |
| Restart recovery | 1.150 s in-process; 3.539 s after PostgreSQL restart | Passed |
| Global concurrency | 100 maximum | Passed, configured limit 100 |
| Dashboard p95 | 639 ms at 30 days; 1,310 ms at 90 days | Passed, limit 3 s |

The two memory windows completed 415,500 and 416,000 admitted operations. Both ended below their starting private-memory value and neither grew strictly monotonically. Per-host and per-IP limits are covered by the loopback `SafeHttpTransportTests.ConcurrencyLimiter_QueuesAboveConfiguredGlobalHostAndAddressBounds` regression.

## Correctness and failure evidence

| Required behavior | Evidence |
|---|---|
| One terminal result and safe competing finalization | Ordered PostgreSQL checks for logical execution, aggregate writing and competing finalization |
| No duplicate active monitors, issue states, incidents, notification events or deliveries | PostgreSQL uniqueness and concurrency stages |
| No prohibited-network escape or unauthorized connection | `SafeHttpTransportTests` mixed-answer, rebinding, redirect, revoked-authorization and fallback-authorization cases |
| Certificate-controlled networking remains zero | Both `SslCertificateProbeTests.CertificateValidation_DoesNotContactCertificateControlledUrls` cases |
| Unknown monitor and snapshot types fail before networking | Exhaustive execution dispatch and `CheckSnapshotTargetTests` unknown-version cases |
| Historical, cross-type and stale results cannot mutate current state | Snapshot-generation, stale HTTP/SSL, cross-monitor and current-disposition PostgreSQL stages |
| Active, current, held and comparison-baseline evidence survives retention | AC-14 ordered retention stages for all twelve categories |
| Enqueue interruption and outstanding-work restart need no repair | Representative failure on acknowledgement 250 followed by 500-ID reconciliation without duplicate rows |
| PostgreSQL outage needs no repair | Disposable server stop, confirmed refusal, same-directory restart and 500-item reconciliation |
| Lease expiry, duplicate delivery and finalization replay converge | Ordered execution, fencing, notification and incident stages |
| DNS/address, HTTP, redirect, slow/truncated body and TLS failures stay bounded | Controlled loopback `SafeHttpTransportTests` and `SslCertificateProbeTests` suites |
| Retention interruption and concurrent mutation recover safely | Cancellation, advisory-lock retry, notification-dispatch and incident-reopen PostgreSQL stages |

Worker-stop boundaries are exercised through persisted checkpoint simulations: committed work before enqueue, leased work during execution, idempotent delivery after finalization and outstanding work on restart. The PostgreSQL outage is a real disposable-process stop and restart. No third-party target was contacted.

## Delivery evidence

| Command | Result |
|---|---|
| `scripts/run-delivery-checks.ps1` | Passed; 832 unit tests and 667 ordinary integration tests; six expected opt-in skips |
| `npm test` | 39 passed |
| `scripts/run-database-foundation-tests.ps1` | Full ordered 22-stage gate passed in 1 min 13 s |
| Representative load test | Passed in 30 min 55 s |
| PostgreSQL restart recovery test | Passed in 3 s |
| Release build and EF checks | Zero warnings/errors; no pending model changes; compiled model current |
| Security checks | Repository secret scan and package vulnerability policy passed |
| Changed UI | Endpoint policy/permission, diagnostics and retention desktop/mobile browser evidence recorded in `Monitoring_Hardening_Evidence.md` |

AC-14 is satisfied by the aggregate-before-delete retention gate. AC-15 is satisfied by the passing scripted unit, integration, JavaScript, migration, security and representative tests recorded above.

## Limits

This evidence covers a single Windows development machine, one PostgreSQL instance and controlled local fixtures. It does not demonstrate high availability, multi-node scheduling, disaster recovery, point-in-time restore, managed-secret integration, centralized telemetry export, formal on-call operation or a public production rollout. Retention remains disabled and dry-run by default until explicitly commissioned. Hosted workflow execution is not claimed; the repository delivery workflow and equivalent local script are present.
