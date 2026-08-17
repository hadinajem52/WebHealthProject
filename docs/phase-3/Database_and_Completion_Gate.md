# Phase 3 database and completion gate

**Work item:** Phase 3 / WI-32 completion gate  
**Rules:** BR-S01, BR-S02, BR-S05, BR-S07, BR-S08  
**Acceptance criteria:** AC-02, AC-05

## Verification result

The Phase 3 completion gate passed on 2026-08-17. The repeatable delivery workflow passed with Testcontainers, and the separate native PostgreSQL clean-database gate passed. The accepted SSH.NET advisory remains explicitly allow-listed by the delivery script.

| Required evidence | Verification |
|---|---|
| Cadence and one-check catch-up boundaries | `MonitorCadenceTests` verifies exact slots and defaults; the PostgreSQL scheduling gate verifies one overdue check, future anchored `NextDueAt`, and no immediate backlog. |
| Controlled HTTP outcomes and policy rules | `HttpResultNormalizerTests` covers 2xx, configured statuses, 4xx, 5xx, markers, body limits, primary precedence, and stable issue keys. |
| Redirect safety | `SafeHttpTransportTests` covers loops, exact hop limits, missing and unsupported locations, prohibited/unauthorized redirect targets, and production HTTPS downgrade. |
| Bounded response handling | `SafeHttpTransportTests` covers delay timeout, caller cancellation, premature/chunked response read failure handling, malformed compression, response-header limits, and decoded-body limits. |
| DNS, address, and proxy boundaries | `DestinationAddressPolicyTests`, `SafeHttpTransportTests`, and the Phase 0 safe-HTTP spike cover prohibited IPv4/IPv6 ranges, mixed answers, rebinding, pinned IPv4/IPv6 connection addresses, and proxy bypass prevention. |
| Leases, duplicate delivery, and recovery | The PostgreSQL foundation gate covers competing lease acquisition, fencing, duplicate delivery, exhausted work, retry recovery, and one terminal result per logical check. |
| Scheduling recovery | The PostgreSQL scheduling gate covers interrupted enqueue recovery, restart reconciliation with the same logical-check ID, and competing dispatchers. |
| Ineligible target suppression | The PostgreSQL scheduling gate verifies that disabled client, website, environment, endpoint, or monitor, and expired target authorization, create no new scheduled check. |
| Sensitive-data exclusion | The transport contract excludes raw exceptions, headers, cookies, query values, credentials, and content; the PostgreSQL history gate verifies no response-body column is persisted and result/redirect evidence is bounded and query-free. |
| Database repeatability | `run-database-foundation-tests.ps1` applies all migrations to a clean native PostgreSQL database and proves a second application is a no-op. |
| Full delivery workflow | `run-delivery-checks.ps1 -UseTestcontainers` performs locked restore, formatting, Release build, pending-model check, all tests including Testcontainers, vulnerability policy, secret scan, and whitespace validation. |

## Recorded runs

- `./scripts/run-delivery-checks.ps1 -UseTestcontainers`: passed — 81 unit tests; 92 integration tests passed, with the separate native-database test intentionally skipped in that run; Testcontainers migration verification passed.
- `./scripts/run-database-foundation-tests.ps1`: passed — clean native PostgreSQL migration application and repeated-application no-op verification passed.

This completes Phase 3. Phase 4 remains responsible for health projection, incidents, maintenance, and notifications.
