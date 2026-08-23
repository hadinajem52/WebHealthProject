# AJAX implementation plan for WebHealthProject


## Verdict

AJAX fits this project well, but it should be implemented as **progressive enhancement over the existing ASP.NET Core MVC application**, not as a SPA rewrite.

The recommended stack is:

* Native browser `fetch`
* Razor partial views for complex UI
* JSON only for data-only endpoints and background-job status
* Existing MVC actions, authorization policies, application services, and validation
* Normal forms, links, and redirects retained as the no-JavaScript fallback

This matches the repository’s current architecture: .NET 10 MVC, server-rendered Razor views, a small frontend surface, and only Chart.js as a JavaScript dependency.

I would **not** introduce React, Vue, Axios, or a client-side router for this work. They would add a second application architecture without removing the need for the existing MVC views.

---

# 1. What the repository currently does

## Server-rendered MVC with progressive enhancement

The web layer is cleanly separated from the Application, Infrastructure, and Domain layers. AJAX behavior should therefore stay inside `WebHealth.Web`; it should not move validation or business rules into JavaScript.

There are currently only two application JavaScript files:

* `shell.js`
* `dashboard.js`

`shell.js` enhances navigation drawers, menus, time-zone display, flash dismissal, password reveal, confirmations, and form-dependent fields. `dashboard.js` renders Chart.js charts from data already embedded in the HTML. Both files are deliberately designed around server-rendered fallback behavior.

## Most mutations use POST–redirect–GET

The main controllers generally follow this sequence:

1. Submit a form.
2. Run an application service.
3. place a message in `TempData`.
4. Redirect to a list or details page.
5. Render the entire layout again.

This pattern appears in registry, targets, checks, incidents, maintenance, administration, notifications, and PageSpeed actions.

The TempData flash implementation is specifically built for a later redirected response. AJAX responses will therefore need a first-class message path rather than relying only on TempData.

## Several pages are natural AJAX candidates

The repository already has GET-based filtering or selection on:

* Dashboard

* Websites

* Endpoints

* Incidents

* SEO

* Audit trail

* Crawls

* PageSpeed

There is also already a JSON precedent: `ReportsController.Trend` returns dashboard trend data as JSON.

---

# 2. Recommended AJAX architecture

The browser should follow this path:

```text
Normal Razor page
    ↓
Form or link marked with data-ajax-*
    ↓
Shared ajax.js intercepts the interaction
    ↓
Existing controller action and application service run
    ↓
Controller returns:
    - Razor partial for UI updates
    - ProblemDetails for errors
    - JSON for queued-job status
    ↓
Only the affected page region is replaced
    ↓
Shell/page JavaScript initializes the new fragment
```

The important principle is:

> **AJAX changes how a response is rendered, not how the business operation is performed.**

The same service calls, authorization policies, validation, audit logging, and concurrency checks must run for both normal and AJAX requests.

## New client-side foundation

Add:

```text
src/WebHealth.Web/wwwroot/js/
├── shell.js
├── ajax.js
├── dashboard.js
├── incidents.js
└── page-audits.js
```

### `ajax.js`

This should own:

* Intercepting explicitly marked forms and links
* Serializing GET filters into query strings
* Submitting POST forms using `FormData`
* Loading and disabled-button states
* `AbortController` support
* Replacing target regions
* Updating browser history
* Handling `popstate`
* Rendering server messages
* Handling 401, 403, 404, 409, 422, and 500 responses
* Initializing newly inserted content
* Preventing duplicate submissions

Do not globally intercept every form and link. Only enhance elements with explicit attributes such as:

```html
<form data-ajax-form
      data-ajax-target="#endpoint-results"
      data-ajax-history="push">
```

That prevents logout, CSV downloads, external links, destructive forms, and unfamiliar future controls from accidentally being handled as AJAX.

## Refactor `shell.js` into an initializer

Most of `shell.js` currently binds controls once during DOM-ready. Injected action menus, password controls, validation summaries, and time elements would not automatically receive those behaviors.

Refactor it toward:

```javascript
window.WebHealth = window.WebHealth || {};

window.WebHealth.init = function (root) {
    // Initialize controls under root only.
};
```

Requirements:

* `init(document)` runs on initial load.
* `init(newFragment)` runs after an AJAX swap.
* Initialization must be idempotent.
* Controls should carry a marker such as `data-initialized="true"` when necessary.
* Prefer delegated events for forms, links, confirmation buttons, and flash dismissal.
* Keep direct listeners only for stateful widgets such as popup menus.

The time-zone formatter must also be callable for newly inserted `<time>` elements.

## AJAX request identification

Send a consistent same-origin header:

```http
X-WebHealth-Ajax: 1
```

Create a small server helper:

```text
src/WebHealth.Web/Ajax/
├── AjaxRequestExtensions.cs
├── AjaxResponseHeaders.cs
└── AjaxFragmentViewModel.cs
```

Controllers can then choose between a full view and a partial without duplicating the operation itself:

```csharp
return Request.IsWebHealthAjax()
    ? PartialView("_EndpointResults", model)
    : View(model);
```

---

# 3. Response contract

Use a small, predictable contract throughout the application.

| Situation                                | Response                                            |
| ---------------------------------------- | --------------------------------------------------- |
| GET filter or pagination succeeds        | `200` with Razor HTML fragment                      |
| Same-page mutation succeeds              | `200` with updated Razor fragment                   |
| Background job queued                    | `202` with JSON containing run ID and status URL    |
| Form validation fails                    | `422` with the form partial and validation messages |
| User is not authenticated                | `401`                                               |
| User lacks permission                    | `403`                                               |
| Entity does not exist                    | `404`                                               |
| Optimistic concurrency conflict          | `409`                                               |
| Unexpected failure                       | `application/problem+json` with correlation ID      |
| Successful creation requiring navigation | local redirect header or JSON `redirectUrl`         |

## Authentication redirect handling

The cookie configuration currently redirects unauthenticated users to `/Account/Login` and unauthorized users to the access-denied route. In a normal request, that is correct. In a fetch request, it can cause the browser to follow the redirect and return a login page with HTTP 200, which the AJAX code might then insert into the current page.

Configure cookie events so requests carrying `X-WebHealth-Ajax: 1` receive:

* `401` instead of a login redirect
* `403` instead of an access-denied redirect

The client should then show an appropriate message or perform a deliberate full-page navigation to login.

## Antiforgery protection

The application globally enables automatic antiforgery validation for controller actions.

Preserve this by submitting the actual Razor form through `new FormData(form)`. The generated `__RequestVerificationToken` hidden field will then travel with the request. Avoid switching normal form POSTs to JSON unless you also introduce a deliberate token-header convention.

## Flash messages

For AJAX responses, do not depend exclusively on TempData.

A partial response can include:

```html
<div data-ajax-flashes>
    <!-- Same accessible flash markup as _FlashMessages -->
</div>

<div id="incident-content" data-ajax-region>
    <!-- Updated region -->
</div>
```

The client should:

1. Extract and insert the messages into the layout’s message region.
2. Announce success through `role="status"`.
3. Announce failures through `role="alert"`.
4. Replace the requested content region.
5. Initialize the inserted fragment.

Normal redirected requests can continue using TempData unchanged.

---

# 4. Correct fragment boundaries

Do not always update the smallest possible element. The replacement boundary must contain all state that can become stale.

## Dashboard: replace one coherent dashboard region

`HomeController` intentionally computes cards, tables, certificates, diagnostics, trends, and active incidents from the same normalized report query. Updating those pieces with unrelated requests could make the cards describe a different data snapshot than the table or incident list.

Create:

```text
Views/Home/
├── Index.cshtml
└── _DashboardContent.cshtml
```

The filter can request `_DashboardContent` as one response. After replacement:

* Reapply local-time formatting.
* Destroy old Chart.js instances.
* Build charts from the newly returned data.
* Update the URL with the active filters.
* Focus the error summary when filtering is invalid.

## Incident details: replace the full incident workspace

The incident page has several forms sharing the same hidden `version` value. Acknowledge, resolve, reassign, add note, close, force-close, and reopen can all change the incident version and available actions. The page also includes the timeline and notification history.

After any incident mutation, replace the complete incident detail stack rather than only changing one badge. This automatically refreshes:

* Status
* Version tokens
* Available actions
* Owner
* Timeline
* Evidence
* Notification records

This is safer than manually patching eight separate DOM locations.

## Endpoint details: replace status and action regions together

The endpoint detail page contains:

* Run-check actions
* Disable/archive/restore actions
* Pause/resume scheduling
* Version fields
* Endpoint status
* Monitoring eligibility
* Certificate state

At minimum, place the overview and action menus in one `_EndpointOverview` partial. A pause, resume, disable, restore, or archive action should replace that complete region so the version and allowed actions remain synchronized.

## Lists: replace results, summary, and pagination

For endpoint, incident, SEO, audit, crawl, and website filters, the result fragment should include:

* Applied-filter summary
* Empty state or table
* Result count
* Pagination
* Any per-row actions

Do not replace only `<tbody>` because empty states, captions, pagination, and result summaries also change.

---

# 5. Page-by-page implementation matrix

| Area               | AJAX behavior                                                     | Replacement scope                                        | Priority    |
| ------------------ | ----------------------------------------------------------------- | -------------------------------------------------------- | ----------- |
| **Endpoints**      | Search without reload; Run now; pause/resume/disable/archive      | Entire results region or endpoint overview/action region | Highest     |
| **Incidents**      | Filtering, pagination, lifecycle actions, notes, reassignment     | Result list or complete incident workspace               | Highest     |
| **PageSpeed**      | Endpoint selection, Run now, live queued/running/completed status | PageSpeed content region                                 | Highest     |
| **Dashboard**      | Apply filters and redraw all dashboard data                       | Entire dashboard content below filter                    | High        |
| **Notifications**  | Mark all read; remove unread indicators immediately               | Notifications component                                  | High        |
| **Checks**         | History pagination; queue manual checks; latest-result refresh    | History results or latest-check panel                    | High        |
| **SEO**            | Filters and pagination                                            | SEO results region                                       | Medium      |
| **Audit trail**    | Filters and pagination                                            | Audit results region                                     | Medium      |
| **Crawl reports**  | Endpoint selection and broken-link pagination                     | Crawl report/results region                              | Medium      |
| **Registry**       | Website tag filtering; client/website state changes               | List or detail region                                    | Medium      |
| **Maintenance**    | Cancel window; later create/edit forms                            | Details region, then form region                         | Medium      |
| **Administration** | User/team forms after the pattern is proven                       | Form or list region                                      | Later       |
| **Account**        | Keep login and logout as normal navigation                        | Full page                                                | Do not AJAX |
| **CSV export**     | Browser download                                                  | Normal request                                           | Do not AJAX |

The endpoint list already combines a GET search form with per-row manual-check POST forms, making it an excellent first pilot page.

---

# 6. Background jobs and polling

## PageSpeed should be the first polling implementation

The PageSpeed page currently tells the user to reload while an audit is running. It already models queued, active, completed, failed, and already-running states.

Change `RunNow` so the AJAX path returns:

```json
{
  "runId": "...",
  "statusUrl": "/PageAudits/Status?...",
  "message": "PageSpeed audit queued."
}
```

with HTTP `202 Accepted`.

Add a status action that returns either:

* `202` plus the active-status partial while still running
* `200` plus the completed PageSpeed content when terminal
* `404` if the run is not visible to the caller

Polling behavior:

* Start quickly after queueing.
* Gradually reduce frequency.
* Stop when the run completes or fails.
* Pause while the page is hidden.
* Pause while the browser is offline.
* Abort when the user selects a different endpoint.
* Set a maximum polling lifetime.
* Provide a manual refresh button as fallback.

Start with polling rather than SignalR. `Program.cs` currently has no hub registration, and adding push infrastructure solely for this feature would be unnecessary complexity at this stage.

## Manual checks

`ChecksController` queues availability and certificate checks and redirects back to the endpoint page.

Initially, AJAX should:

* Disable the button while submitting.
* Show “Check queued.”
* Close the action menu.
* Prevent repeated clicks.

Later, add a bounded latest-check polling endpoint when a stable queued-check identifier or timestamp can be used to distinguish the new run from the previous one.

---

# 7. Optimistic concurrency handling

Registry and target commands carry explicit `Version` values, and `RegistryMutationStatus` includes `ConcurrencyConflict`.

The existing concurrency tests deliberately preserve the submitted stale version and return the form with an error rather than silently replacing user input.

AJAX must preserve this behavior.

### For edit forms

Return `409` with the submitted form partial and a conflict message:

> This record changed after you opened it. Your submitted values have been preserved. Reload the current version before trying again.

Do not silently overwrite the user’s inputs with the new database values.

### For button-style state changes

For pause, resume, acknowledge, restore, or disable:

* Return `409`.
* Fetch the latest details fragment.
* Replace the stale region.
* Announce that the record changed before the operation could be applied.

---

# 8. Phased rollout

## Phase 0 — Shared foundation

Implement before converting individual pages:

1. Add `ajax.js`.
2. Add `Request.IsWebHealthAjax()`.
3. Define response status conventions.
4. Make cookie authentication AJAX-aware.
5. Add an accessible global AJAX message region.
6. Refactor `shell.js` into an idempotent fragment initializer.
7. Add standard loading and disabled-button styles.
8. Add controller tests for full-page and AJAX variants.
9. Document the data-attribute contract.

This should be completed as one focused foundation change rather than invented separately in every page.

## Phase 1 — Read-only filters and pagination

Convert the safest GET interactions first:

1. Endpoint search
2. Website tag filtering
3. Incident filters and pagination
4. SEO filters and pagination
5. Audit filters and pagination
6. Check-history pagination
7. Crawl selection and pagination
8. PageSpeed endpoint selection
9. Dashboard filtering

Requirements:

* Query string remains the source of truth.
* Update `history.pushState`.
* Handle browser Back and Forward through `popstate`.
* Preserve copyable and bookmarkable URLs.
* Abort an older filter request when a newer one starts.
* Keep the normal Submit button functional without JavaScript.

## Phase 2 — Immediate same-page mutations

Convert:

1. Mark notifications read
2. Incident lifecycle actions
3. Incident notes and reassignment
4. Endpoint pause/resume
5. Endpoint disable/archive/restore
6. Maintenance cancellation
7. Manual check queueing

After each successful mutation, return or retrieve the authoritative updated fragment. Do not make the browser guess the resulting status.

## Phase 3 — Asynchronous job status

Implement:

1. PageSpeed queue and polling
2. Manual check completion refresh
3. Optional notification-menu refresh
4. Optional latest endpoint-status refresh

Do not add global polling to every page. Poll only while a known operation is active.

## Phase 4 — Create and edit forms

Convert forms in this order:

1. Clients
2. Websites
3. Environments
4. Maintenance windows
5. Endpoints
6. Teams
7. Users

Endpoint, user, and team forms should be late because they have the most validation, permission, sensitive-field, or concurrency complexity.

Validation flow:

* Submit with `FormData`.
* Return `422` with the form partial.
* Replace only the form.
* Focus the validation summary.
* Preserve entered values.
* Reinitialize conditional fields.
* On success, navigate to the new details URL through an explicit local redirect response.

## Phase 5 — Hardening

Complete:

* Network failure UI
* Retry behavior
* Duplicate-submit prevention
* 401/403 session handling
* 409 conflict behavior
* Correlation ID display
* Browser-history tests
* Keyboard and screen-reader verification
* Slow-request and out-of-order-response testing
* Payload-size and request-count measurement

---

# 9. Testing strategy

The repository already uses xUnit, `Microsoft.AspNetCore.Mvc.Testing`, FluentAssertions, PostgreSQL test containers, and separate unit/integration test projects.

Extend that foundation with three layers.

## Controller and integration tests

For every enhanced action, test both:

* Normal request without the AJAX header
* AJAX request with the header

Cover:

* Antiforgery rejection
* Authorization policies
* Unauthenticated requests
* Forbidden requests
* Validation errors
* Not found
* Concurrency conflicts
* Successful partial response
* Normal redirect fallback
* Background-job `202`
* Safe local redirects only

## JavaScript unit tests

Test the shared client logic for:

* Form serialization
* Target selection
* Response-status mapping
* Request cancellation
* Double-submit blocking
* URL/history updates
* Fragment initialization

## Browser tests

Add a small browser-level suite for behavior that controller tests cannot prove:

* Filtering updates results without a full navigation.
* The URL changes correctly.
* Back and Forward restore previous filters.
* Validation moves focus to the summary.
* Loading states are announced.
* Double clicks create only one operation.
* A stale version produces conflict UI.
* A queued PageSpeed run updates to completed.
* Session expiry does not insert a login page into a card.
* Core forms still work with JavaScript disabled.

---

# 10. Important implementation risks

| Risk                                              | Required protection                                                       |
| ------------------------------------------------- | ------------------------------------------------------------------------- |
| Stale hidden `version` fields                     | Replace the full stateful region after mutations; return 409 on conflict  |
| Duplicate event listeners                         | Idempotent initialization or delegated events                             |
| Out-of-order search responses                     | `AbortController` and request sequence checking                           |
| Duplicate queued jobs                             | Disable submit and enforce server-side idempotency/already-running checks |
| Login HTML inserted into page                     | AJAX-specific 401/403 cookie behavior                                     |
| TempData message lost                             | Return messages in AJAX fragments                                         |
| Dashboard sections describing different snapshots | Return one composite dashboard fragment                                   |
| Chart.js instances retained after replacement     | Destroy old chart instances before rebuilding                             |
| Injected times remain in UTC                      | Reapply the current time-zone preference to new fragments                 |
| Back button stops reflecting UI                   | Query strings plus `pushState` and `popstate`                             |
| JavaScript failure makes controls unusable        | Preserve normal links, forms, and controller redirects                    |
| Overly broad DOM replacement                      | Replace named `data-ajax-region` elements, never the whole layout         |

---

# 11. What should remain normal navigation

AJAX should not literally replace every HTTP navigation.

Keep these as standard requests:

* Login
* Logout
* Access-denied navigation
* CSV export
* Main sidebar navigation
* Opening a separate details page
* External links
* File downloads
* Permanent purge when the current entity no longer exists afterward

The account controller has lockout handling, safe return URLs, authentication-cookie state changes, and explicit login/logout redirects. There is little benefit in AJAX-enhancing those actions.

---

# Recommended first implementation slice

The strongest first implementation is:

1. Build the shared AJAX foundation.
2. Refactor `shell.js` to initialize fragments.
3. Convert **Endpoints search** to partial loading.
4. Convert **Run now** on the endpoint list to AJAX.
5. Convert **Mark notifications read**.
6. Add full-request and AJAX-request integration tests.
7. Verify that all three interactions still work with JavaScript disabled.

That slice tests the three essential patterns with limited risk:

* GET filtering
* POST mutation
* Shared layout component update

Once those contracts are stable, incidents, dashboard filters, PageSpeed polling, registry mutations, and CRUD forms can reuse the same infrastructure instead of accumulating page-specific AJAX code.
