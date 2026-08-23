# AJAX implementation evidence

Date: 2026-08-23

Branch: `ajax-implementation`

Plan: `docs/General/Ajax-plan.md`

Contract: `docs/General/Ajax-contract.md`

## Work-item coverage

| ID | Scope | Result |
| --- | --- | --- |
| AJAX-01 | Shared request detection, response headers, fragments, messages, status handling, local redirects, cancellation, history, busy state, and retry behavior | Complete |
| AJAX-02 | Idempotent shell and fragment initialization, timezone reapplication, action-menu disposal, and dashboard chart lifecycle | Complete |
| AJAX-03 | Dashboard, endpoint, website, incident, SEO, audit, check-history, crawl, and PageSpeed GET filters and pagination | Complete |
| AJAX-04 | Notifications, incident lifecycle, registry lifecycle, maintenance cancellation, and endpoint scheduling mutations | Complete |
| AJAX-05 | Manual-check and PageSpeed `202 Accepted` contracts with bounded polling and authoritative fragment refresh | Complete |
| AJAX-06 | Client, website, environment, endpoint, maintenance, team, and user create/edit forms | Complete |
| AJAX-07 | `401`, `403`, `404`, `409`, `422`, Problem Details, correlation references, and safe redirect behavior | Complete |
| AJAX-08 | Normal-request fallback for every enhanced controller action | Complete |
| AJAX-09 | JavaScript, unit, integration, PostgreSQL foundation, and Chrome end-to-end verification | Complete |

## User-visible and authorization behavior

- Enhanced reads update only their named content region, keep query strings authoritative, and restore state through Back and Forward.
- Mutations disable their submit control, block duplicate POSTs, refresh authoritative server-rendered content, and announce the result through live status or alert regions.
- Validation and concurrency responses remain on the form. Validation focuses the summary; stale submitted values are preserved for non-sensitive forms.
- Unauthenticated AJAX requests return `401` and deliberately navigate to the normal login page. Forbidden requests return `403`; login markup is never inserted into an application fragment.
- Existing controller policies, antiforgery validation, assignment scope, administrator-only operations, SSRF controls, and local-redirect restrictions remain server-side.

## Inputs, outputs, validation, and errors

- GET forms serialize successful controls into query strings and push history.
- POST forms submit `FormData` with antiforgery tokens and return JSON navigation, JSON refresh instructions, or bounded HTML fragments.
- Invalid forms return `422`; optimistic concurrency conflicts return `409`; queued work returns `202` with a stable run identifier and status URL.
- GET network failures offer a safe Retry action. Ambiguous POST failures offer Reload page and never automatically repeat the mutation.
- Error payloads can include a correlation reference without exposing exception details.

## Automated verification

| Command | Result |
| --- | --- |
| `dotnet test WebHealthProject.sln --no-restore` | 633 unit tests passed; 415 integration tests passed; 4 expected infrastructure-gated tests skipped |
| `npm test` | Syntax checks passed; 11 JavaScript tests passed |
| `scripts/run-database-foundation-tests.ps1` | Passed on a clean isolated PostgreSQL database; all 22 ordered stages completed |

The four normal solution-test skips are the PostgreSQL Testcontainer guard, dedicated database-foundation guard, SMTP delivery guard, and reporting performance guard. The database foundation test was run separately through its required script and passed.

The automated coverage includes both normal and AJAX requests, antiforgery and authorization failures, unauthenticated and forbidden responses, validation, not-found, concurrency, partial success, full-page redirects, safe redirects, job `202` responses, request cancellation, out-of-order responses, duplicate-submit prevention, fragment initialization, history updates, retry safety, and menu closure.

## Chrome end-to-end verification

Chrome tested the application against the isolated `webhealth_ajax_browser` PostgreSQL database on port 6545 and the local HTTPS application on port 7144.

| Scenario | Evidence | Result |
| --- | --- | --- |
| Authentication | Administrator signed in through the normal login form and reached authenticated pages | Passed |
| Client validation | Empty create returned `422`, kept the create URL, replaced one form region, and focused the `role=alert` summary | Passed |
| CRUD navigation | Client, website, production environment, endpoint, and maintenance window were created through enhanced forms; success messages survived the deliberate detail-page navigation | Passed |
| Website edit | Website was enabled after its active environment was created; the detail page showed Version 2 and Enabled | Passed |
| Normalization | Duplicate `AJAX`/`ajax` website tags were stored and rendered once | Passed |
| Endpoint filtering | Empty and matching searches updated the result region and URL without reloading layout assets | Passed |
| History | Back restored the empty endpoint search and Mobile PageSpeed view; Forward restored the matching search and Desktop view | Passed |
| Request count | An enhanced endpoint filter produced one `/Targets/Endpoints` request; a deliberate full navigation produced the document request plus CSS, JavaScript, font, and image requests | Passed |
| Manual check | Rapid double-submit produced exactly one Manual logical check; polling replaced Latest check with HTTP 200, 101 ms, Warning, and Manual source | Passed |
| Certificate refresh | The endpoint detail refreshed to a valid certificate with hostname and chain validation | Passed |
| PageSpeed | One action queued Mobile and Desktop runs; both completed, each scored 80/100 with Lighthouse 13.4.1 and one history row | Passed |
| Loading feedback | The PageSpeed control was disabled during submission, then the live region announced queued and completed states | Passed |
| Endpoint lifecycle | Pause and Resume updated effective monitoring authoritatively and closed the action menu after each accepted mutation | Passed |
| Notifications | Mark all as read changed the shared header from one unread notification to none unread without a redundant success banner | Passed |
| Incident lifecycle | Acknowledge, add note, and resolve all refreshed the workspace; the append-only timeline advanced from three to six events | Passed |
| Maintenance lifecycle | A one-off endpoint window was created in `Asia/Beirut`, then cancelled through AJAX with Version 2 and Cancelled status | Passed |
| Concurrency | Two tabs opened Client Version 1; the first saved Version 2 and the stale second form returned `409`, focused its alert, and preserved both submitted fields | Passed |
| GET outage and Retry | Stopping the app produced a network alert with Retry; after restart Retry restored results and the URL | Passed |
| Retry regression | The first manual run exposed a stale disabled error alert after successful recovery; commit `0b5eb34` clears the old alert, and the complete outage/recovery flow passed after the fix | Passed |
| Session expiry | Signing out in another tab caused the stale AJAX filter to receive `401` and navigate to `/Account/Login?returnUrl=%2FTargets%2FEndpoints`; the page had one login form, no application shell navigation, and no `#ajax-page` fragment | Passed |
| Accessibility | Validation focus, live status/alert semantics, disabled queued controls, and menu expanded/collapsed state were verified from the accessibility tree | Passed |
| Console | Final Chrome warning/error log was empty | Passed |

The Chrome controller does not expose a per-tab JavaScript-disable switch. JavaScript-off compatibility was therefore verified through the full-request integration variants for every enhanced action; no persistent Chrome content setting was changed.

## Data, security, operations, and compatibility

- This work adds no database schema or migration changes.
- The isolated browser data contains only local demonstration records and monitoring evidence.
- Credentials are not recorded in source, test output, or this evidence document.
- Request logs showed the expected `200`, `202`, `401`, `409`, and `422` contracts. Background-job logs recorded stable PageSpeed run identifiers, strategies, completion, Lighthouse version, item counts, and failed-audit counts.
- The local Mailgun sandbox rejected delivery to an unauthorized recipient during the isolated run. Notification delivery remained retryable and did not affect the AJAX contract or browser results.
- The implementation retains native links, forms, controller redirects, and antiforgery behavior for compatibility when JavaScript is unavailable.

## Self-review

The review checked stale response suppression after body parsing, GET cancellation, POST duplicate blocking, busy-state versioning, safe retry behavior, fragment initializer idempotency, chart disposal, menu-listener disposal, menu closure, TempData success-message persistence, sensitive password clearing, and local-only navigation. The Chrome-discovered retry-message defect was fixed, covered by a JavaScript regression assertion, committed separately, and retested end to end.
