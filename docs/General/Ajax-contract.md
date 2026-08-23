# AJAX progressive-enhancement contract

The AJAX layer changes rendering only. Controllers keep the same authorization, validation, application-service calls, antiforgery protection, and normal redirects used without JavaScript.

## Request attributes

`data-ajax-form` enhances one form. `data-ajax-link` enhances one link. Neither contract applies globally.

`data-ajax-target` names the stable region returned by the server. The default page region is `#ajax-page`.

`data-ajax-history="push"` updates the query string after a successful GET. Back and Forward reload the region from the URL.

Every enhanced request sends `X-WebHealth-Ajax: 1`. Razor renders the selected view without the shared layout for that request, leaving the target region as the response body.

Only the newest read request for a target may update it. A newer read aborts an older read. Mutations are serialized per target, cannot be aborted by reads, and delay same-target reads until the mutation and its authoritative refresh finish. Form submitters remain disabled while their mutation is active.

A read network failure renders a same-request Retry action. An ambiguous mutation failure never offers an automatic retry: its target and submitter remain locked until the user reloads or navigates away. Newly inserted fragments pass through the shared shell initializer before focus is moved to their validation summary, error region, updated control, heading, or fragment root. Background polling does not steal focus.

## Responses

| Status | Contract |
|---|---|
| 200 | HTML fragment or JSON result |
| 202 | Queued operation with JSON status metadata or active HTML fragment |
| 401 | Session is missing; the client deliberately navigates to login |
| 403 | Permission denied |
| 404 | Record is not visible or no longer exists |
| 409 | Concurrency conflict; submitted forms are preserved or stateful regions are refreshed |
| 422 | HTML form fragment with validation messages, or JSON error with an authoritative refresh instruction |
| 500 | Problem Details with a correlation identifier |

JSON operations use `AjaxFragmentViewModel`: `message`, `level`, `redirectUrl`, `refreshUrl`, `statusUrl`, and `runId`. Redirect and refresh addresses must be same-origin.

Successful GET history defaults to `push`. Refresh-only reads use `data-ajax-history="replace"` so repeated refreshes do not add duplicate Back-button entries.

Normal login, logout, access-denied navigation, sidebar navigation, details-page links, exports, downloads, external links, and permanent purge remain standard requests.

## Live run status

A page showing work that is still running refreshes itself instead of asking the reader to reload. `poller.js` owns the schedule — backoff, pause while the tab is hidden or the browser is offline, and a bounded lifetime. `run-status.js` drives it from markup; `checks.js` drives it from the manual-check JSON status contract above.

`data-run-status` marks the region to refresh. It needs an `id`, because that id is the selector the response is read from. On it:

| Attribute | Meaning |
|---|---|
| `data-run-active` | `true` while work is in progress. Polling starts only when it is `true` and stops on the first response that is not. |
| `data-run-url` | The address to re-read. It may be the page's own URL; the response is a fragment because the request carries `X-WebHealth-Ajax: 1`. |
| `data-run-also` | Extra regions replaced from the same response, so a control outside the region can change with it. |
| `data-run-lifetime` | Maximum polling lifetime in milliseconds. After it, polling stops with a message telling the reader to reload. |
| `data-run-complete-message` | Flash message rendered once, when a poll observes the work has finished. |

`data-run-scope` on an ancestor names the region's selector. Any enhanced request originating inside that ancestor stops polling, and a `202` JSON response carrying `statusUrl` starts it — that is how a Run-now button begins polling before its region has been re-rendered.

`data-ajax-target` accepts a comma-separated list, and every named region must be present in the response or none is replaced. Polling requests are reads: they never move focus, and a newer request for the same target aborts an older one.

A run still in progress is drawn with a turning mark rather than a still badge, so a page that refreshes itself is distinguishable from one that has stopped updating. `StatusBadgeViewModel.Pending` renders it inside a badge; `_Spinner` renders it beside a card status line or inside a button.
