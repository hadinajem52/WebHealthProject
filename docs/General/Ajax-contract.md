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
