# PageSpeed Insights SEO Audit Integration - Implementation Plan

**Repository:** `hadinajem52/WebHealthProject`  
**Reviewed baseline:** `main` at commit `c6f2dd0fb22705d499e03bd2165afc75aa193638`  
**Suggested repository path:** `docs/phase-7/PageSpeed_Insights_SEO_Audit_Implementation_Plan.md`  
**Prepared:** 2026-08-20  
**Status:** Proposed  
**Estimated implementation:** 8-11 working days for the V1 described here.

> **Scope note.** This is a solo portfolio/internship feature. The plan deliberately stops at working, tested software demonstrable on a local or demo instance. Metrics backends, staged rollouts, quota alerting, and incident integration are explicitly out of scope and are recorded as deferred in section 14, not designed here.

---

## 1. Goal

Add a PageSpeed Insights-backed technical SEO audit feature that:

- calls Google's official PageSpeed Insights `runPagespeed` API;
- runs the Lighthouse `seo` category for selected WebHealth endpoints;
- stores a bounded, normalized audit result rather than the complete provider response;
- shows the latest score, audit details, run history, and score delta;
- supports both scheduled and authorized on-demand runs;
- does not slow or starve the existing availability, SSL, robots, or crawl workers;
- keeps the existing WebHealth SEO checks as the authoritative policy-aware checks;
- can later replace Google PageSpeed Insights with another provider, including self-hosted Lighthouse, without redesigning the application.

The user-facing score must be labelled **Lighthouse technical SEO score**, not a general ranking or traffic score. It is an automated technical audit and must not be presented as a prediction of search ranking.

---

## 2. Executive architecture decision

### 2.1 Selected approach

Build a new, bounded `PageAudits` subsystem with a provider abstraction and a dedicated Hangfire queue.

```text
Endpoint
   |
   +--> existing HTTP check --> SeoObservation --> WebHealth SEO policy findings
   |
   `--> PageAuditTarget --> page-audits queue --> IPageAuditProvider
                                                  |
                                                  `--> PageSpeedInsightsProvider
                                                         |
                                                         `--> runPagespeed API
                                                                  |
                                                                  v
                                                        normalized PageAuditRun
                                                        + PageAuditItem rows
                                                                  |
                                                                  v
                                                          score/history/delta
```

### 2.2 Decisions for V1

- Provider: Google PageSpeed Insights API v5.
- API method: `runPagespeed`.
- Lighthouse category: `seo` only.
- Strategy: `mobile` only. The schema allows `desktop` without a migration; the UI does not offer it.
- Locale: fixed to `en-US` so stored titles and descriptions are stable.
- Eligibility: public, non-authenticated HTTP/HTTPS pages only.
- Scheduling default: disabled per endpoint until explicitly enabled.
- Suggested cadence after enablement: once every 24 hours.
- On-demand runs: supported for users authorized to test the endpoint.
- Storage: normalized values only; no full raw JSON, screenshots, traces, or HTML.
- Provider details: include the Lighthouse version on every run.
- CrUX fields: deliberately not modelled in this feature.
- Existing WebHealth SEO rules remain authoritative for expected canonical host, production/non-production indexing policy, descriptions, robots, and sitemap behavior.
- Incident/notification bridge: deferred, see section 14.

### 2.3 Why this should not be added directly to `SeoObservation`

The existing `SeoObservation` has a precise meaning: values extracted from the bounded body of an ordinary successful endpoint check. It is keyed to a `LogicalCheck`, carries the policy used at finalization, and deliberately cannot retain page markup.

A PageSpeed audit has different semantics:

- it is initiated independently from the ordinary HTTP check;
- it is executed by Google infrastructure, not by `SafeHttpTransport`;
- it can take much longer than a health check;
- it has a provider version and Lighthouse version;
- it returns a collection of audits, including manual and not-applicable entries;
- it can fail because of provider quota, provider timeout, CAPTCHA, or Lighthouse runtime errors even when the endpoint is healthy.

Mixing both result types into `SeoObservation` would make one table mean two different things and would tie an external provider's lifecycle to the availability finalization transaction.

### 2.4 Why V1 should not become a third `EndpointMonitor` type

The current logical-check pipeline is intentionally exhaustive for HTTP availability and SSL:

- `MonitorWorkKinds` maps only HTTP and SSL monitor types;
- `MonitoringSchedulingService` explicitly claims only those two types;
- durable work kinds and recovery SQL explicitly list HTTP and SSL;
- `LogicalCheckJob` is fixed to the short-check queue;
- `LogicalCheckExecutionService` branches between HTTP transport and SSL probing;
- finalization and observation persistence are built around those evidence types.

Adding PageSpeed to that path would require widening core availability scheduling, durable work, execution evidence, finalization, migrations, and recovery logic. It would also place a long third-party API call inside infrastructure designed and named for short checks.

The repository already establishes the correct precedent for long-running work: crawling has its own contracts, persistence, job, queue, and worker pool. Page audits should follow that pattern.

---

## 3. Codebase assessment

The following existing areas were reviewed and should guide the implementation.

### 3.1 Existing SEO implementation

- `src/WebHealth.Domain/Seo/SeoExtractionRules.cs`
- `src/WebHealth.Application/Seo/ISeoValueExtractor.cs`
- `src/WebHealth.Infrastructure/Seo/SeoValueExtractor.cs`
- `src/WebHealth.Application/Seo/SeoRuleEvaluator.cs`
- `src/WebHealth.Application/Seo/SeoFindingGroups.cs`
- `src/WebHealth.Infrastructure/Seo/SeoEntities.cs`
- `src/WebHealth.Infrastructure/Seo/SeoEntityConfigurations.cs`
- `src/WebHealth.Application/Seo/ISeoReader.cs`
- `src/WebHealth.Infrastructure/Seo/SeoReader.cs`
- `src/WebHealth.Web/Controllers/SeoController.cs`
- `src/WebHealth.Web/Models/SeoViewModels.cs`
- `src/WebHealth.Web/Views/Seo/Index.cshtml`
- `docs/phase-6/SEO_Value_Extraction.md`
- `docs/phase-6/SEO_Canonical_And_Indexing_Policy.md`

The new feature must complement these checks rather than duplicate or override their policy decisions.

### 3.2 Scheduling and worker isolation patterns

- `src/WebHealth.Infrastructure/Monitoring/MonitoringSchedulingService.cs`
- `src/WebHealth.Infrastructure/Monitoring/LogicalCheckJob.cs`
- `src/WebHealth.Infrastructure/Monitoring/ManualCheckService.cs`
- `src/WebHealth.Infrastructure/Seo/SeoSchedulingOptions.cs`
- `src/WebHealth.Infrastructure/Crawling/CrawlSchedulingOptions.cs`
- `src/WebHealth.Infrastructure/Crawling/CrawlRunJob.cs`
- `src/WebHealth.Infrastructure/DependencyInjection.cs`
- `src/WebHealth.Web/Program.cs`
- `docs/phase-6/Crawl_Execution_And_Isolation.md`

The crawler's dedicated queue and separate Hangfire server are the closest pattern for PageSpeed work.

### 3.3 Persistence and lifecycle patterns

- `src/WebHealth.Infrastructure/Persistence/ApplicationDbContext.cs`
- `src/WebHealth.Infrastructure/Crawling/CrawlEntities.cs`
- `src/WebHealth.Infrastructure/Crawling/CrawlEntityConfigurations.cs`
- `src/WebHealth.Infrastructure/Registry/EndpointPurgeCascade.cs`
- `src/WebHealth.Infrastructure/Registry/RegistryEntities.cs`
- `src/WebHealth.Infrastructure/Registry/RegistryEntityConfigurations.cs`

The PageSpeed schema should follow the repository conventions: explicit table names, bounded text, named constraints, filter columns on the row being queried, database-enforced state contracts, and explicit endpoint purge ordering.

### 3.4 Registry and authorization patterns

- `src/WebHealth.Application/Registry/TargetContracts.cs`
- `src/WebHealth.Infrastructure/Registry/EndpointRegistryService.cs`
- `src/WebHealth.Web/Models/TargetRegistryViewModels.cs`
- `src/WebHealth.Web/Views/Targets/_EndpointForm.cshtml`
- `src/WebHealth.Application/Authorization/AuthorizationPolicies.cs`
- `src/WebHealth.Infrastructure/Registry/TargetAuthorizationService.cs`

Configuration changes belong behind `ManageRegistry`; on-demand execution should use the same endpoint test authorization used by existing manual checks.

### 3.5 Test and delivery patterns

- `tests/WebHealth.UnitTests`
- `tests/WebHealth.IntegrationTests`
- `tests/WebHealth.IntegrationTests/Support/DatabaseFoundationAssertions.cs`
- `.github/workflows/delivery.yml`
- `scripts/run-delivery-checks.ps1`

CI must use recorded JSON fixtures and fake HTTP handlers. It must not call the live PageSpeed API.

---

## 4. External API contract

### 4.1 Endpoint

Use the official service endpoint and REST method:

```http
GET https://pagespeedonline.googleapis.com/pagespeedonline/v5/runPagespeed
```

V1 request parameters:

```text
url=<absolute public endpoint URL>
category=seo
strategy=mobile
locale=en-US
key=<API key>
```

Example shape:

```http
GET /pagespeedonline/v5/runPagespeed
    ?url=https%3A%2F%2Fexample.com%2F
    &category=seo
    &strategy=mobile
    &locale=en-US
    &key=REDACTED
```

Rules:

1. Always send `category=seo`. If no category is supplied, the API defaults to Performance.
2. Always send `strategy`; do not depend on the API's desktop default.
3. Always send a stable locale for persisted text.
4. Build the query with a URI/query builder. Do not concatenate an unescaped endpoint URL.
5. The service host and path are constants, not deployment configuration. This prevents a configuration change from turning the client into a general outbound HTTP client.

### 4.2 Response fields to consume

Only read the fields needed by WebHealth:

```text
captchaResult
analysisUTCTimestamp
lighthouseResult.requestedUrl
lighthouseResult.finalUrl
lighthouseResult.lighthouseVersion
lighthouseResult.runWarnings
lighthouseResult.categories.seo.score
lighthouseResult.categories.seo.auditRefs[]
lighthouseResult.audits[<audit id>]
lighthouseResult.runtimeError.code
lighthouseResult.runtimeError.message
```

For each audit referenced by `categories.seo.auditRefs`, consume:

```text
audit.id
audit.title
audit.description
audit.score
audit.scoreDisplayMode
audit.displayValue
audit.explanation
audit.errorMessage
```

Do not iterate over and persist every member of `lighthouseResult.audits`. Use the SEO category's `auditRefs` as the membership list.

### 4.3 Fields deliberately excluded

Do not create V1 persistence models for:

- `loadingExperience`;
- `originLoadingExperience`;
- CrUX metrics or distributions;
- screenshots;
- filmstrips;
- traces;
- free-form audit `details`;
- stack packs;
- `timing.total` and provider duration;
- `version.major` / `version.minor` (the Lighthouse version is the version that matters for comparability);
- the full Lighthouse result;
- the complete PageSpeed response.

Google states that CrUX field data is planned for removal from the PageSpeed Insights API and recommends the dedicated CrUX APIs. This feature is an SEO audit integration, so it should depend only on the Lighthouse result.

### 4.4 Provider error classification

Normalize failures into bounded WebHealth categories. Suggested vocabulary:

| Category | Trigger | Retry? |
|---|---|---:|
| `ProviderRateLimited` | HTTP 429 | Yes, honor `Retry-After` when usable |
| `ProviderUnavailable` | HTTP 500/502/503/504 | Yes |
| `ProviderTimeout` | client timeout, HTTP 408, or Lighthouse protocol timeout | Yes |
| `ProviderAuthenticationFailed` | HTTP 401/403 caused by key/project configuration | No |
| `TargetRejected` | HTTP 400 with a valid provider response indicating an invalid or unsupported target | No |
| `CaptchaBlocked` | `captchaResult` indicates blocking/needed | Usually no immediate retry |
| `LighthouseRuntimeError` | `runtimeError.code` is present and not `NO_ERROR` | Depends on code; default no immediate retry after bounded attempts |
| `ProviderContractInvalid` | missing SEO category, malformed score, unknown required shape | No automatic retry after bounded attempts |
| `ProviderResponseTooLarge` | response exceeds the configured byte cap | No |
| `ProviderResponseInvalid` | malformed JSON or invalid values | No automatic retry after bounded attempts |
| `Cancelled` | worker/application cancellation | No; leave an explicit cancelled run |
| `UnknownProviderFailure` | safe fallback | At most the normal transient attempt limit |

Never store Google's complete error body. Store a bounded safe diagnostic that excludes the API key and excludes any raw response content not explicitly approved.

---

## 5. Proposed domain and application contracts

Create a new top-level feature area rather than placing provider-specific types under `Seo`.

### 5.1 Suggested folders

```text
src/WebHealth.Domain/PageAudits/
src/WebHealth.Application/PageAudits/
src/WebHealth.Infrastructure/PageAudits/
src/WebHealth.Web/Views/PageAudits/
```

### 5.2 Vocabulary

Suggested file:

```text
src/WebHealth.Domain/PageAudits/PageAuditVocabulary.cs
```

Define constants or validated value objects for:

```text
Providers: PageSpeedInsights
Categories: Seo
Strategies: Mobile, Desktop
Sources: Scheduled, Manual
RunStatuses: Queued, Running, Completed, CompletedWithWarnings, Failed, Cancelled
AuditStatuses: Passed, Failed, Scored, Manual, NotApplicable, Informative, Error
Comparability: Comparable, LighthouseVersionChanged
FailureCategories: the provider categories in section 4.4
```

Use case-sensitive stored values with database constraints, matching the repository's current style.

### 5.3 Provider-neutral request and result

Two interfaces earn their place in V1: `IPageAuditProvider`, which is the provider-swap seam the goal statement requires, and `IPageAuditReader`, which keeps `RegistryVisibility` scoping testable. The scheduling, execution, and configuration services have one implementation and one caller each and should be concrete classes.

Suggested contracts:

```csharp
public sealed record PageAuditRequest(
    Uri TargetUrl,
    string Category,
    string Strategy,
    string Locale);

public sealed record PageAuditProviderResult(
    string Provider,
    string RequestedUrl,
    string FinalUrl,
    DateTimeOffset AnalysisAt,
    string LighthouseVersion,
    decimal? CategoryScore,
    IReadOnlyList<PageAuditProviderItem> Items,
    IReadOnlyList<string> Warnings,
    string? RuntimeErrorCode,
    string? RuntimeErrorMessage);

public sealed record PageAuditProviderItem(
    string AuditId,
    string Title,
    string Description,
    decimal? Score,
    string ScoreDisplayMode,
    double Weight,
    string? Group,
    string? DisplayValue,
    string? Explanation,
    string? ErrorMessage);

public interface IPageAuditProvider
{
    string ProviderName { get; }

    Task<PageAuditProviderResult> RunAsync(
        PageAuditRequest request,
        CancellationToken cancellationToken = default);
}
```

No Google JSON type should escape the infrastructure assembly.

### 5.4 Normalization

Suggested pure domain functions:

```text
src/WebHealth.Domain/PageAudits/PageAuditNormalization.cs
    NormalizeCategoryScore
    ClassifyAuditStatus
    BoundText
src/WebHealth.Domain/PageAudits/PageAuditEligibility.cs
    Evaluate
```

Rules:

- Category score must be null or between 0 and 1.
- Persist the raw decimal score; derive the 0-100 display score in the read model.
- `binary` score 1 -> `Passed`.
- `binary` score below 1 -> `Failed`.
- `numeric` -> `Scored`; V1 must not invent pass/fail semantics for a numeric audit.
- `manual` -> `Manual`.
- `not_applicable` -> `NotApplicable`.
- `informative` -> `Informative`.
- `error`, an audit error message, or an invalid score -> `Error`.
- Unknown display modes should be stored as `Error` or rejected as a provider-contract failure; they must not silently become failures.
- Only an audit classified as `Failed` belongs in the failed-audit list.
- Manual audits do not affect automated failure counts.

The structured-data audit is an example of a manual Lighthouse SEO audit and should be presented separately from failed automated audits.

---

## 6. Persistence design

### 6.1 `page_audit_target`

One configured provider/category/strategy audit for one endpoint.

Suggested columns:

| Column | Type/meaning |
|---|---|
| `id` | UUID primary key |
| `endpoint_id` | UUID FK to endpoint |
| `provider` | `PageSpeedInsights` |
| `category` | `Seo` |
| `strategy` | `Mobile` or `Desktop` |
| `is_enabled` | Entire audit target can run |
| `scheduling_enabled` | Scheduled runs enabled; manual runs may still be allowed |
| `interval_seconds` | Cadence |
| `schedule_anchor` | Stable cadence anchor |
| `next_due_at` | Indexed scheduler claim field |
| `created_at`, `updated_at` | Lifecycle timestamps |
| `version` | Optimistic concurrency |

Who changed the configuration is recorded by the audit events in section 12.4, so this table does not carry `created_by_user_id` / `updated_by_user_id`.

Constraints and indexes:

- Unique `(endpoint_id, provider, category, strategy)`.
- `interval_seconds` within documented bounds, suggested 6 hours to 30 days.
- Index `(is_enabled, scheduling_enabled, next_due_at, id)` or a partial index covering enabled scheduled targets.
- Provider/category/strategy enumerations enforced by named checks.

V1 UI creates or updates only a `Mobile` + `Seo` + `PageSpeedInsights` row. The schema remains ready for desktop without a migration.

### 6.2 `page_audit_run`

One scheduled or manual request and its terminal outcome.

Suggested columns:

| Column | Type/meaning |
|---|---|
| `id` | UUID primary key |
| `page_audit_target_id` | FK to target |
| `endpoint_id` | Denormalized filter and purge column |
| `source` | Scheduled or Manual |
| `initiated_by_user_id` | Nullable for scheduled runs |
| `status` | Queued, Running, Completed, CompletedWithWarnings, Failed, Cancelled |
| `requested_url` | Endpoint URL used for the request |
| `final_url` | Provider's final audited URL |
| `raw_score` | Nullable decimal 0-1; the display score is derived |
| `provider` | Provider snapshot |
| `category` | Category snapshot |
| `strategy` | Strategy snapshot |
| `locale` | Locale snapshot |
| `lighthouse_version` | Tool version used for the run |
| `warning_summary` | Bounded summary of run warnings, not raw JSON |
| `attempt_count` | Bounded number of execution attempts |
| `failure_category` | Normalized failure vocabulary |
| `safe_diagnostic` | Bounded safe explanation; carries runtime error code/message and CAPTCHA facts |
| `queued_at` | Queue time |
| `analysis_at` | Provider analysis timestamp |
| `finished_at` | Terminal time |
| `lease_token` | Nullable worker claim token |
| `lease_expires_at` | Nullable claim expiry |
| `updated_at` | Reconciliation field |

The provider snapshot columns exist so run history stays interpretable after the target's configuration changes; the comparability rule in section 11 reads them directly.

Deliberately not stored: a separate integer `score` (derive it), `pagespeed_major_version` / `pagespeed_minor_version`, `captcha_result` and `runtime_error_code` / `runtime_error_message` as dedicated columns (folded into `failure_category` + `safe_diagnostic`), `warning_count` (derive it), `provider_duration_ms`, `started_at`, and `hangfire_job_id`.

Constraints and indexes:

- Raw score is null or between 0 and 1.
- Attempt count is non-negative and capped by application configuration.
- Terminal status requires `finished_at`; non-terminal status must not have it.
- Completed status requires a score and no failure category.
- Failed status requires a recognized failure category.
- Lease token and lease expiry are both null or both present.
- Index `(page_audit_target_id, finished_at DESC, id DESC)` for latest/history reads.
- Index `(endpoint_id, finished_at DESC)` for endpoint reports.
- Index `(status, updated_at)` for queue reconciliation.
- Partial unique index allowing at most one active Queued/Running run per target.

### 6.3 `page_audit_item`

One normalized SEO audit in one run.

Suggested columns:

| Column | Type/meaning |
|---|---|
| `id` | UUID primary key |
| `run_id` | FK to run |
| `audit_id` | Stable Lighthouse audit identifier |
| `status` | Normalized WebHealth status |
| `score` | Nullable decimal 0-1 |
| `score_display_mode` | Provider mode snapshot |
| `weight` | Category audit weight |
| `group_name` | Optional category group |
| `title` | Bounded human title |
| `description` | Bounded human description |
| `display_value` | Bounded formatted value |
| `explanation` | Bounded explanation |
| `error_message` | Bounded error text |

Per-item warning columns are not stored; Lighthouse SEO audits rarely populate them and run-level warnings are already summarized on the run.

Suggested bounds:

```text
audit_id: 200
score_display_mode: 40
group_name: 100
title: 500
description: 2,000
display_value: 1,000
explanation: 2,000
error_message: 1,000
```

Constraints and indexes:

- Unique `(run_id, audit_id)`.
- Score null or between 0 and 1.
- Weight non-negative.
- Index `(run_id, status, audit_id)` for the details view.

### 6.4 No raw provider payload table

V1 must not add a `raw_json`, `report_json`, screenshot, trace, or arbitrary `details` column.

Reasons:

- PageSpeed responses can be large.
- Audit `details` is deliberately free-form and version-dependent.
- Screenshots and traces provide little value for the SEO-only category.
- Retaining the full response increases storage and privacy risk.
- The normalized schema is sufficient for score history, failed audits, manual checks, and comparison.

During development, redacted fixture files belong in the test project, not the database.

### 6.5 DbContext and purge changes

Modify:

- `src/WebHealth.Infrastructure/Persistence/ApplicationDbContext.cs`
- `src/WebHealth.Infrastructure/Registry/EndpointPurgeCascade.cs`

Add DbSets for the three new entities. In the endpoint purge sequence, delete:

1. `page_audit_item` rows for endpoint runs;
2. `page_audit_run` rows;
3. `page_audit_target` rows;
4. then continue with endpoint deletion.

Add database foundation assertions proving no new foreign key blocks endpoint purge and that no API key or raw JSON column exists.

---

## 7. Provider implementation

### 7.1 New infrastructure files

Suggested files:

```text
src/WebHealth.Infrastructure/PageAudits/PageSpeedInsightsOptions.cs
src/WebHealth.Infrastructure/PageAudits/PageSpeedInsightsProvider.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditResponseReader.cs
```

### 7.2 Configuration

Suggested application configuration without the secret:

```json
{
  "PageAudits": {
    "Scheduling": {
      "Enabled": false,
      "WorkerCount": 1,
      "DispatchBatchSize": 10,
      "ReconciliationBatchSize": 25,
      "ReconciliationDelay": "00:05:00",
      "DefaultInterval": "1.00:00:00",
      "MaximumAttempts": 3
    },
    "PageSpeedInsights": {
      "Locale": "en-US",
      "RequestTimeout": "00:01:30",
      "MaximumResponseBytes": 10485760
    }
  }
}
```

Secret name:

```text
PageAudits__PageSpeedInsights__ApiKey
```

Use .NET user secrets locally. Do not place the key in `appsettings.json` or any committed file.

Startup behavior:

- If Page Audits scheduling is disabled, the API key may be absent.
- If scheduling is enabled, fail startup when the API key is empty.
- An authorized on-demand run should report configuration unavailable rather than attempting an anonymous API call when no key is configured.
- Validate all bounds at startup using the same explicit-validation style as current scheduling options.

### 7.3 HttpClient registration

Register a dedicated typed or named client. Do not reuse `SafeHttpTransport`.

`SafeHttpTransport` exists to contact user-configured monitored targets under authorization, DNS, redirect, concurrency, and SSRF rules. The PageSpeed client contacts one fixed Google API host, while the monitored URL is sent as a query value.

Requirements:

- Fixed HTTPS base address: `https://pagespeedonline.googleapis.com/`.
- No automatic redirects, or only redirects constrained to the same Google API origin.
- Request timeout from validated options.
- `HttpCompletionOption.ResponseHeadersRead`.
- Response body byte cap before JSON deserialization.
- JSON depth and audit-count caps.
- No custom logging of the full request URI.
- Never include the key in an exception, log property, audit event, diagnostic, or persisted URI.
- Use a deterministic user agent identifying WebHealth without credentials.

No new third-party NuGet package is required. `HttpClient` and `System.Text.Json` are sufficient.

### 7.4 Parsing strategy

Do not mirror the complete discovery schema.

Parse with `JsonDocument` behind a focused `PageAuditResponseReader` that reads only the fields in section 4.2. The reader must:

- tolerate unknown properties;
- reject missing required SEO category data;
- bound dictionaries and arrays;
- treat audit `details` as ignored data;
- preserve null scores for manual/not-applicable audits;
- validate score ranges;
- never expose provider JSON types outside Infrastructure.

### 7.5 API key hardening

Setup must:

- enable the PageSpeed Insights API in a Google Cloud project;
- create a dedicated key for WebHealth;
- apply an API restriction allowing only the PageSpeed Insights API;
- keep the key out of source control, test fixtures, screenshots, and support exports.

The key is passed as the documented `key` query parameter. Because that places it in the request URI, WebHealth must not log the full provider request URI. Existing Microsoft logging is currently raised to Warning, but the PageSpeed client must still avoid any explicit URL logging and should have a regression test over recorded logs.

---

## 8. Eligibility and privacy

### 8.1 Eligibility rules

Create a pure `PageAuditEligibility` decision. A target is eligible only when:

- the endpoint exists and is not deleted;
- the endpoint and owning environment/site/client are active according to existing registry rules;
- the endpoint uses HTTP or HTTPS;
- the URL has no credentials or fragment, already guaranteed by normalization;
- target authorization is current;
- the PageAudit target is enabled;
- the host is suitable for a public third-party audit;
- no active run already exists for the same audit target.

Public-only restrictions should reject or mark unsupported:

- `localhost`;
- loopback addresses;
- private, link-local, multicast, or otherwise non-public literal IP addresses;
- single-label internal host names;
- `.local` and similar explicitly internal names;
- pages that require an authenticated session.

Do not attempt to prove public reachability by calling the target from the PageSpeed scheduling transaction. The provider response is the source of truth for whether Google can load it.

### 8.2 Third-party disclosure

Enabling PageSpeed audit sends the configured endpoint URL to Google, and Google's infrastructure loads that URL.

The endpoint form should state this directly, for example:

> PageSpeed auditing sends this public URL to Google PageSpeed Insights and asks Google infrastructure to load it. Do not enable it for private, secret, authenticated, or internal-only URLs.

Do not send query strings containing secrets. The repository already normalizes and redacts monitored URLs; the PageSpeed request must use the stored normalized endpoint URL and must not accept an arbitrary URL from the browser form or controller action.

### 8.3 Target authorization

Even though WebHealth contacts Google rather than directly loading the page, an audit is still active testing of a configured target. Use the existing target authorization service for:

- enabling PageSpeed auditing;
- executing an on-demand run;
- scheduled eligibility checks.

A caller-supplied endpoint ID is a parameter, not permission.

---

## 9. Scheduling, queue isolation, and idempotency

### 9.1 New options and queue

Suggested files:

```text
src/WebHealth.Infrastructure/PageAudits/PageAuditSchedulingOptions.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditSchedulingService.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditRunJob.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditSchedulingApplicationBuilderExtensions.cs
```

Queue name:

```text
page-audits
```

### 9.2 Dedicated Hangfire server

Register a separate Hangfire server that serves only `page-audits`, following the crawler isolation pattern.

Suggested worker count: 1.

Do not list `page-audits` on the current monitoring/notification/maintenance/robots worker server. Queue ordering does not reserve capacity; a separate server is what guarantees that a long PageSpeed call cannot take a short-check worker.

### 9.3 Dispatcher

A recurring dispatcher should:

1. claim due `page_audit_target` rows with `FOR UPDATE SKIP LOCKED`;
2. re-check endpoint eligibility and target authorization;
3. insert one Queued `page_audit_run` per eligible target;
4. snapshot provider, category, strategy, locale, and requested URL onto the run;
5. advance `next_due_at` from the stable schedule anchor;
6. commit;
7. enqueue jobs.

Do not hold a database transaction open while calling Google.

### 9.4 Reconciliation

Enqueue-after-commit has the same failure window as any durable job producer. Add a reconciliation path for runs that are:

- still Queued after the reconciliation delay;
- Running with an expired lease;
- left non-terminal by worker termination.

Reconciliation should re-enqueue the same `run_id`, not create another run. The run job is idempotent and claims the row by lease token, so a spurious re-enqueue is harmless. This is why the run does not need to store a Hangfire job ID.

### 9.5 Active-run uniqueness

Enforce at most one Queued or Running run per target with a partial unique index. This prevents:

- a recurring dispatcher and a manual request from starting the same profile simultaneously;
- duplicate scheduler instances from creating duplicate work;
- a reconciliation sweep from creating a second run.

The manual-run UI should return the existing active run rather than reporting an ambiguous failure.

### 9.6 Explicit bounded retry

Use an explicit retry policy rather than unlimited Hangfire automatic retry.

Suggested maximum: 3 total attempts.

Suggested backoff:

```text
attempt 1 -> immediate
attempt 2 -> after 60 seconds
attempt 3 -> after 5 minutes
```

Use `Retry-After` for 429/503 when it is present, valid, and inside a safe maximum delay.

The job should persist attempt count and the latest normalized failure category. On a transient failure before the final attempt, schedule the same run ID again and keep the run non-terminal. On the final failure, mark the run Failed with a bounded diagnostic.

`AutomaticRetry(Attempts = 0)` is recommended so the application's recorded attempt count and Hangfire behavior cannot diverge.

### 9.7 Lease/idempotency behavior

The run job should atomically claim a run only when:

- status is Queued; or
- status is Running and the previous lease expired.

It should write a new lease token and expiry, commit, and then call the provider outside the transaction.

When the provider returns:

1. normalize the result in memory;
2. open a short transaction;
3. verify the lease token still belongs to the worker;
4. insert the run's audit items;
5. update run summary fields and terminal status;
6. clear the lease;
7. commit.

A duplicate job that sees a terminal run returns successfully without doing any work.

---

## 10. Execution and normalization flow

### 10.1 Execution service

Suggested file:

```text
src/WebHealth.Infrastructure/PageAudits/PageAuditExecutionService.cs
```

Flow:

```text
claim run
  -> re-check endpoint and authorization
  -> create provider-neutral request from stored snapshot
  -> call IPageAuditProvider
  -> validate provider/category/strategy response
  -> normalize score and referenced SEO audits
  -> persist PageAuditItem rows
  -> complete run
```

Comparison against the previous run is a read-model concern (section 11) and is not computed or stored during execution.

### 10.2 Do not accept an arbitrary execution URL

The job receives only `run_id` and attempt metadata. It loads the snapshotted requested URL from the run. It must not receive a browser-supplied URL or construct the target from request data at job execution time.

### 10.3 Audit membership

The correct algorithm is:

```text
seoCategory = lighthouseResult.categories["seo"]
for each auditRef in seoCategory.auditRefs:
    audit = lighthouseResult.audits[auditRef.id]
    normalize audit with auditRef.weight and group
```

If a referenced audit is missing, treat the provider contract as incomplete. Do not silently omit it from the score explanation.

### 10.4 Run completion status

Suggested rules:

- `Completed`: valid score, no runtime error, no run warnings.
- `CompletedWithWarnings`: valid score and audit set, but provider supplied run warnings.
- `Failed`: no trustworthy category result due to provider/target/runtime failure.
- `Cancelled`: explicit cancellation before terminal persistence.

A failed Lighthouse audit is not a failed PageAudit run. It is a successfully completed run containing a failed audit item.

### 10.5 Score presentation

Persist the raw category score and derive the display score in the read model:

```text
raw category score: 0.92
user display score: 92
```

Use one documented rounding rule and test boundary values. Suggested rule:

```text
round(raw_score * 100, MidpointRounding.AwayFromZero)
```

Do not recalculate Google's category score from audit weights. Persist the category score returned by Lighthouse and separately persist the audit weights for explanation.

---

## 11. Comparison behavior

A run is directly comparable only to the latest earlier completed run with the same endpoint, provider, category, strategy, and locale, **and the same Lighthouse major version**. A major-version change can add, remove, or redefine audits.

The read model returns:

```text
current score
previous score
absolute delta
comparability: Comparable | LighthouseVersionChanged
```

When the Lighthouse major version changed, still show the delta but label it as spanning a tool-version change.

Deliberately deferred until there is real score data to look at: new/continuing/resolved failure buckets, per-target score thresholds, minimum-delta rules, and any automated regression classification. V1 shows the current failed audits and the score delta; that is enough evidence to decide those rules later.

---

## 12. Registry and configuration UI

### 12.1 Endpoint configuration

Add a PageSpeed section to the endpoint form, separate from WebHealth's own SEO policy fields.

Suggested controls:

```text
[ ] Enable Google PageSpeed SEO audits
[ ] Run PageSpeed audits on a schedule
Strategy: Mobile (V1; hidden field)
Interval: 24 hours (optional Administrator override)
```

Do not overload the existing `SchedulingEnabled` field. That field controls the endpoint's ordinary scheduled monitors. PageSpeed scheduling is separate.

The endpoint update service should create/update the matching `page_audit_target` row in the same transaction as the endpoint configuration and audit event.

### 12.2 Application contracts

Rather than adding many optional PageSpeed properties to `Endpoint`, add a nested application contract:

```csharp
public sealed record PageAuditSettings(
    bool Enabled,
    bool SchedulingEnabled,
    string Strategy,
    int IntervalHours);
```

Then add it to `EndpointDetails`, `CreateEndpoint`, and `UpdateEndpoint`. All create/update/read paths must use one definition.

### 12.3 Validation

Validate:

- strategy is supported;
- interval is within safe bounds;
- PageSpeed audit cannot be enabled for an ineligible public URL;
- enabling requires current target authorization;
- scheduling cannot be enabled while the target itself is disabled;
- only users with `ManageRegistry` can change configuration;
- the API key is never part of registry form data.

### 12.4 Audit trail

Record bounded audit events for:

```text
PageAuditEnabled
PageAuditDisabled
PageAuditScheduleEnabled
PageAuditScheduleDisabled
PageAuditIntervalChanged
```

Do not record the API key or provider request URI.

---

## 13. Read model and web UI

### 13.1 Suggested controller

Add:

```text
src/WebHealth.Web/Controllers/PageAuditsController.cs
```

Authorization:

- read actions: `ReadRegistry`;
- configuration: existing registry management actions under `ManageRegistry`;
- `RunNow`: `TestRegistryTargets` plus a service-level authorization check.

Suggested routes/actions:

```text
GET  /PageAudits?endpointId=<id>&runId=<optional id>
POST /PageAudits/RunNow/<endpointId>
```

Unauthorized endpoint/run IDs should return Not Found where appropriate, matching the crawl reader's non-disclosure pattern.

### 13.2 Application reader

Suggested files:

```text
src/WebHealth.Application/PageAudits/IPageAuditReader.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditReader.cs
```

Every reader method must apply `RegistryVisibility` in the database before returning any run or item.

Suggested operations:

```text
GetEndpointSummaryAsync(endpointId, access)
ListRunsAsync(endpointId, page, access)
ListAuditItemsAsync(runId, access)
```

The summary computes the score delta and comparability against the previous comparable run. Bounds belong in the reader, not only in the view.

### 13.3 Single PageSpeed page

One view, `Views/PageAudits/Index.cshtml`, following the dashboard card style.

Header/summary area:

- endpoint selector;
- enabled/disabled and scheduling state;
- provider and strategy;
- latest run status;
- current Lighthouse technical SEO score;
- previous score, delta, and comparability label;
- Lighthouse version and analysis timestamp;
- counts of failed, passed, manual, not-applicable, informative, and error audits;
- bounded run history list;
- authorized `Run now` action.

Audit detail, for the selected run, in expandable sections on the same page:

1. Failed automated audits.
2. Passed audits.
3. Manual checks.
4. Not-applicable audits.
5. Informative/scored audits.
6. Audit errors and run warnings.

Selecting a run from the history list re-renders the page for that run. A separate run-details view is not needed in V1.

Do not render provider-supplied descriptions as HTML. Store and render them as plain text. Lighthouse descriptions may contain Markdown-style links; V1 should not introduce a Markdown renderer merely for provider text.

### 13.4 Existing SEO page integration

Keep `/Seo` focused on WebHealth's latest extracted values and policy findings.

Add a compact PageSpeed column or secondary action once the PageAudits reader exists, for example:

```text
PageSpeed: 92 Mobile, audited 4h ago
```

The PageSpeed score should link to the PageAudits page. Do not merge Lighthouse audit failures into `SeoFindingGroups` in V1 because those groups currently map stable WebHealth rule keys and feed server-side filtering.

---

## 14. Deferred: incident and notification integration

The existing incident pipeline is based on findings attached to `LogicalCheck` results and issue state attached to an `EndpointMonitor`. PageAudit runs are deliberately independent, and V1 shows score changes in the UI without creating incidents.

If this is picked up later, the preferred direction is to generalize issue evidence so an issue can be observed by either a logical check result or a page audit run, with provider-neutral observations such as `PageAudit.ScoreBelowThreshold` or `PageAudit.AuditFailed.<audit-id>`. That preserves PageAudit's independent execution while reusing issue confirmation, recovery, incidents, and notifications.

Two things are settled now so a later increment does not have to unpick them:

- **Do not** create synthetic HTTP `LogicalCheck`/`CheckResult` rows merely to reach the `Finding` table. A PageSpeed provider outage is not an endpoint availability failure, and synthetic checks would pollute health history and reporting.
- Alert thresholds, audit ownership where Lighthouse overlaps existing WebHealth rules, and confirmation/recovery counts are decided from real run data, not designed up front.

Everything else about this integration is out of scope for this plan.

---

## 15. File-by-file work map

### 15.1 New domain files

```text
src/WebHealth.Domain/PageAudits/PageAuditVocabulary.cs
src/WebHealth.Domain/PageAudits/PageAuditNormalization.cs
src/WebHealth.Domain/PageAudits/PageAuditEligibility.cs
```

### 15.2 New application files

```text
src/WebHealth.Application/PageAudits/PageAuditContracts.cs
src/WebHealth.Application/PageAudits/IPageAuditProvider.cs
src/WebHealth.Application/PageAudits/IPageAuditReader.cs
```

### 15.3 New infrastructure files

```text
src/WebHealth.Infrastructure/PageAudits/PageAuditEntities.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditEntityConfigurations.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditSchedulingOptions.cs
src/WebHealth.Infrastructure/PageAudits/PageSpeedInsightsOptions.cs
src/WebHealth.Infrastructure/PageAudits/PageSpeedInsightsProvider.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditResponseReader.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditExecutionService.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditSchedulingService.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditReader.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditRunJob.cs
src/WebHealth.Infrastructure/PageAudits/PageAuditSchedulingApplicationBuilderExtensions.cs
```

### 15.4 New web files

```text
src/WebHealth.Web/Controllers/PageAuditsController.cs
src/WebHealth.Web/Models/PageAuditViewModels.cs
src/WebHealth.Web/Views/PageAudits/Index.cshtml
```

### 15.5 Modified files

```text
src/WebHealth.Infrastructure/Persistence/ApplicationDbContext.cs
src/WebHealth.Infrastructure/DependencyInjection.cs
src/WebHealth.Web/Program.cs
src/WebHealth.Web/appsettings.json
src/WebHealth.Infrastructure/Registry/EndpointPurgeCascade.cs
src/WebHealth.Application/Registry/TargetContracts.cs
src/WebHealth.Infrastructure/Registry/EndpointRegistryService.cs
src/WebHealth.Infrastructure/Registry/TargetRegistryReader.cs
src/WebHealth.Web/Models/TargetRegistryViewModels.cs
src/WebHealth.Web/Views/Targets/_EndpointForm.cshtml
src/WebHealth.Web/Views/Targets/Endpoint.cshtml
src/WebHealth.Web/Views/Seo/Index.cshtml
```

### 15.6 Migrations

One migration covering schema and endpoint configuration:

```text
PageAuditFoundation
```

Include the model snapshot and database assertion updates.

### 15.7 New tests

```text
tests/WebHealth.UnitTests/PageAuditNormalizationTests.cs
tests/WebHealth.UnitTests/PageAuditEligibilityTests.cs
tests/WebHealth.IntegrationTests/PageSpeedInsightsProviderTests.cs
tests/WebHealth.IntegrationTests/PageAuditSchedulingTests.cs
tests/WebHealth.IntegrationTests/PageAuditExecutionTests.cs
tests/WebHealth.IntegrationTests/PageAuditAuthorizationTests.cs
```

Test fixtures:

```text
tests/WebHealth.IntegrationTests/Fixtures/PageSpeed/success-seo-mobile.json
tests/WebHealth.IntegrationTests/Fixtures/PageSpeed/manual-and-na.json
tests/WebHealth.IntegrationTests/Fixtures/PageSpeed/runtime-error.json
tests/WebHealth.IntegrationTests/Fixtures/PageSpeed/captcha-blocked.json
tests/WebHealth.IntegrationTests/Fixtures/PageSpeed/missing-seo-category.json
```

Fixtures must be reviewed for URLs, tokens, user data, and oversized details before commit.

---

## 16. Increment plan

Each increment should be independently reviewable and leave the application in a valid state.

### 16.1 - Contracts, eligibility, normalization, and fixtures (1-1.5 days)

**Decide first:** public-only limitation, mobile-only V1, fixed locale, no CrUX dependency, no raw JSON retention.

Deliver:

- domain vocabulary;
- provider-neutral contracts;
- eligibility rules;
- score/status normalization;
- API fixture set;
- unit tests for all status mappings and score boundaries.

Acceptance:

- no infrastructure call exists yet;
- every provider display mode has a deliberate mapping;
- manual is not treated as failed;
- numeric is not given invented binary semantics;
- internal/private URLs are rejected as unsupported.

### 16.2 - PageAudit schema and endpoint purge (1.5-2 days)

**Decide first:** state transitions and active-run uniqueness.

Deliver:

- entities and configurations;
- migration;
- DbSets;
- endpoint purge changes;
- database constraints and indexes;
- database foundation assertions.

Acceptance:

- invalid statuses/scores/timestamps are rejected by PostgreSQL;
- one active run per target is enforced;
- endpoint purge removes all PageAudit data in FK-safe order;
- no raw JSON or API key-capable column exists.

### 16.3 - PageSpeed provider adapter (1.5-2 days)

**Decide first:** maximum response bytes and retryable HTTP classifications.

Deliver:

- options validation;
- fixed-origin HttpClient;
- API request builder;
- bounded response reader;
- provider result mapper;
- fake-handler integration tests.

Acceptance:

- request explicitly includes SEO, strategy, and locale;
- target URL is escaped exactly once;
- API key never appears in logs or persisted diagnostics;
- response details/screenshots are ignored;
- malformed/oversized/missing-category responses fail safely;
- CI makes no external Google call.

### 16.4 - Scheduling, queue, lease, execution, and finalization (2-2.5 days)

**Decide first:** worker isolation and enqueue recovery.

Deliver:

- target scheduler;
- dedicated queue and Hangfire server;
- run job;
- lease claim;
- explicit bounded retry;
- queued/running reconciliation;
- execution service with short finalization transaction and audit item insertion;
- concurrency and isolation tests.

Acceptance:

- PageSpeed work never consumes a short-check or crawl worker;
- concurrent dispatchers do not duplicate runs;
- duplicate jobs do not duplicate provider calls while a valid lease exists;
- a failed enqueue is recovered;
- a worker killed during a run leaves recoverable state;
- failed audits complete a run rather than failing it;
- runtime/provider errors fail the run without creating fake audit failures;
- results are idempotent under duplicate job delivery.

### 16.5 - Endpoint configuration and authorized Run now (1-1.5 days)

**Decide first:** whether V1 interval is fixed or Administrator-configurable. This plan assumes a bounded override.

Deliver:

- create/update/read application contracts;
- endpoint form controls and disclosure;
- transactional target configuration;
- audit events;
- manual queue service;
- authorization tests.

Acceptance:

- disabled by default;
- users without ManageRegistry cannot configure it;
- users without TestRegistryTargets cannot run it;
- service-level authorization protects direct requests;
- one active run is returned rather than duplicated;
- no arbitrary URL is accepted by Run now.

### 16.6 - Reader, UI, comparison, and gate (1.5-2 days)

**Decide first:** whether PageAudits gets its own navigation item or is reached from the SEO and endpoint pages. This plan recommends the latter.

Deliver:

- database-scoped reader;
- comparison against the previous comparable run;
- single PageSpeed page with summary, history, and expandable audit sections;
- run-now action and feedback;
- compact link from `/Seo`;
- structured logs;
- API key setup notes appended to this document;
- final authorization, purge, migration, and isolation gate.

Acceptance:

- every read is scoped by `RegistryVisibility` in SQL;
- direct run IDs from another client return Not Found;
- all lists are bounded/paged;
- provider text is HTML-encoded;
- manual and not-applicable items are visibly separate;
- score metadata shows provider, strategy, analysis time, and Lighthouse version;
- a Lighthouse major-version change is labelled on the delta;
- delivery checks pass with no PageSpeed key configured;
- logs expose run ID, endpoint ID, provider status, and failure category but not the key or full provider URI.

---

## 17. Test plan

### 17.1 Unit tests

#### Request and normalization

- category is always SEO;
- strategy is explicit and case-mapped correctly;
- locale is explicit;
- target URLs with paths/query escaping are encoded once;
- scores 0, 0.005, 0.924, 0.995, and 1 map according to the documented rounding rule;
- score outside 0-1 is rejected;
- binary pass/fail mapping;
- numeric maps to Scored;
- manual maps to Manual;
- not-applicable maps to NotApplicable;
- informative maps to Informative;
- error maps to Error;
- unknown mode does not silently become Failed;
- only category auditRefs are selected;
- missing referenced audit is a contract error;
- all text bounds are deterministic.

#### Eligibility

- public HTTPS eligible;
- public HTTP behavior follows the recorded decision;
- localhost rejected;
- loopback/private/link-local literal IP rejected;
- single-label and `.local` host rejected;
- expired authorization rejected;
- deleted/disabled endpoint rejected;
- active run prevents duplicate request.

#### Comparison

- first run has no previous comparison;
- different strategy or locale is not comparable;
- major Lighthouse version change is labelled;
- minor version change remains Comparable;
- delta arithmetic is exact at score boundaries.

### 17.2 Provider integration tests with fake HTTP

- successful mobile SEO fixture;
- requested/final URL distinction;
- run warnings;
- manual and N/A audits;
- null category score;
- runtime error;
- CAPTCHA result;
- 400 target rejection;
- 401/403 key configuration failure;
- 429 with and without `Retry-After`;
- 500/503;
- timeout and cancellation;
- malformed JSON;
- response over byte limit;
- audit array/dictionary over configured count limit;
- missing SEO category;
- missing referenced audit;
- API key redaction in logs/exceptions.

### 17.3 Database integration tests

- all named check constraints;
- unique target tuple;
- one active run partial unique index;
- unique run/audit pair;
- due-run index shape;
- latest-run index shape;
- terminal timestamp contract;
- completed/failed data contract;
- lease pair contract;
- endpoint purge;
- no raw JSON or API key column;
- migrations apply from empty database;
- model snapshot matches.

### 17.4 Scheduling and job tests

- due targets claimed once under concurrent dispatchers;
- ineligible target advances or pauses according to the recorded scheduling rule;
- commit-before-enqueue failure recovered;
- duplicate job returns after terminal completion;
- active lease prevents duplicate execution;
- expired lease can be reclaimed;
- transient retries are bounded;
- permanent failure is not retried;
- cancellation becomes explicit terminal or recoverable state as designed;
- page-audits queue has its own Hangfire server;
- existing monitoring queue still runs while a blocked fake PageSpeed call occupies the audit worker.

### 17.5 Authorization and web tests

- every role's GET access follows `ReadRegistry`;
- configuration requires `ManageRegistry`;
- Run now requires `TestRegistryTargets` and service-level endpoint access;
- another client's endpoint/run is not disclosed;
- anti-forgery on POST;
- provider text encoded;
- paging bounds;
- empty, queued, running, failed, warning, and completed states render;
- existing `/Seo`, `/Crawl`, and endpoint pages regress successfully.

### 17.6 Manual smoke test

A manual, opt-in script may call the real API using a developer secret. It must not run in CI and must:

- audit a controlled public test URL;
- verify the SEO category exists;
- print only non-secret summary fields;
- redact the key.

---

## 18. Logging

Log:

```text
PageAuditRunId
PageAuditTargetId
EndpointId
Provider
Strategy
AttemptNumber
RunStatus
HttpStatusCode when safe
FailureCategory
TotalDurationMs
AuditItemCount
FailedAuditCount
LighthouseVersion
```

Do not log:

```text
API key
full provider request URI
raw provider response
raw HTML
screenshots/traces
unbounded provider error bodies
```

Metrics backends, quota alerting, and provider health checks are out of scope. Do not call PageSpeed from `/health/ready`: a Google outage must not make WebHealth unready. Readiness may verify only that the API key is present when scheduling is enabled and that scheduling options are valid.

Quota is handled in behavior, not alerting: bounded dispatch batch, one worker, explicit 429 retry with `Retry-After`, and the ability to disable scheduling without deleting configuration or history. At one mobile audit per endpoint per day, 100 enabled endpoints is 100 requests per day.

---

## 19. Enablement

The feature ships disabled. To turn it on:

1. Enable the PageSpeed Insights API in a Google Cloud project and create a restricted key.
2. Apply the migration.
3. Set `PageAudits__PageSpeedInsights__ApiKey` via user secrets and `PageAudits:Scheduling:Enabled=true`.
4. Enable one owned public endpoint and use `Run now`.
5. Verify score, items, Lighthouse version, log redaction, and stored row sizes.

Enable additional endpoints from the endpoint form as needed. Keep the default disabled.

---

## 20. Acceptance criteria

### API and provider

- **PA-01:** WebHealth calls the official PageSpeed Insights v5 `runPagespeed` endpoint, not the website UI.
- **PA-02:** Every request explicitly asks for `category=seo`, a strategy, and a stable locale.
- **PA-03:** The API key is externally configured, restricted, and absent from source, logs, diagnostics, database rows, and UI.
- **PA-04:** The client is bound to the official Google service origin and has timeout, response-size, and JSON-shape limits.
- **PA-05:** CI uses fixtures/fakes and never calls Google.

### Scheduling and reliability

- **PA-06:** PageSpeed jobs run on a dedicated `page-audits` worker server.
- **PA-07:** One target cannot have two active runs.
- **PA-08:** Dispatch is concurrency-safe and enqueue failures are recoverable.
- **PA-09:** Job delivery is idempotent and transient retries are bounded.
- **PA-10:** A PageSpeed job cannot consume the existing short-check or crawl worker pool.

### Data

- **PA-11:** A run stores provider/category/strategy/locale/Lighthouse-version snapshots.
- **PA-12:** Only audits referenced by the SEO category are persisted.
- **PA-13:** Full JSON, screenshots, traces, HTML, and free-form audit details are not persisted.
- **PA-14:** Manual, not-applicable, informative, numeric, failed, passed, and error audits remain distinguishable.
- **PA-15:** Endpoint purge removes all PageAudit data and leaves the audit trail behavior unchanged.

### Security and authorization

- **PA-16:** Only configured public HTTP/HTTPS endpoints are eligible.
- **PA-17:** Enabling and scheduled execution require valid target authorization.
- **PA-18:** Run now requires test permission and service-level endpoint authorization.
- **PA-19:** Reads are scoped in the database with `RegistryVisibility`.
- **PA-20:** The endpoint form states that the URL is sent to and loaded by Google infrastructure.

### User experience

- **PA-21:** The latest Lighthouse technical SEO score, strategy, analysis time, provider status, and Lighthouse version are visible.
- **PA-22:** Users can inspect failed, passed, manual, not-applicable, informative, and error audits separately.
- **PA-23:** Users can view bounded run history and the score delta.
- **PA-24:** A Lighthouse major-version change is disclosed on the delta.
- **PA-25:** Existing WebHealth SEO checks and findings continue to work unchanged.

---

## 21. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Google API behavior changes | Use the documented API, provider abstraction, minimal parsing, version snapshots, fixture contract tests |
| Long audit blocks monitoring | Dedicated queue and Hangfire server with one worker |
| API key leaks through query URL | Secret injection, API restrictions, no URI logging, redaction tests, no request persistence |
| Quota exhaustion | Daily opt-in cadence, bounded dispatch, 429 handling, ability to disable scheduling |
| Internal URL disclosed to Google | Public-only eligibility and explicit endpoint-form disclosure |
| Provider response grows unexpectedly | Response byte cap, ignored `details`, no raw response storage |
| Lighthouse audit set changes | Use `auditRefs`, store tool version, compare by stable audit ID, label major version changes |
| Duplicate scheduled/manual runs | Partial unique active-run index and idempotent run ID |
| Enqueue succeeds/fails around DB commit | Queued-run reconciliation and job idempotency |
| Google outage creates false endpoint incidents | Provider failure remains PageAudit diagnostic; do not map it to availability |
| Duplicate SEO findings confuse users | Existing WebHealth rules remain authoritative; Lighthouse shown as a separate layer |
| Numeric audit score misclassified | Preserve `Scored` status rather than inventing a pass threshold |
| Raw provider text causes XSS | Store/render as bounded plain text with HTML encoding |

---

## 22. Documentation

This document is the feature's documentation. Update it in place with:

- the exact request parameters and fields consumed, once the fixtures are recorded;
- the API and Lighthouse versions observed in those fixtures;
- API key setup notes;
- the gate evidence from increment 16.6 (migration, test runs, authorization matrix, worker-isolation test, key redaction test, endpoint purge test).

No additional Phase 7 documents are required for this feature.

---

## 23. Official API and Lighthouse references

The implementation and its contract tests should reference these official documents.

1. **PageSpeed Insights API REST overview**  
   Service endpoint, discovery document, and `runpagespeed` resource:  
   https://developers.google.com/speed/docs/insights/rest

2. **`runPagespeed` API method reference**  
   Request parameters, accepted categories/strategies, response schema, audits, category audit references, display modes, runtime errors, and versions:  
   https://developers.google.com/speed/docs/insights/v5/reference/pagespeedapi/runpagespeed

3. **PageSpeed Insights API getting started**  
   API-key usage, cURL example, response overview, and notice that CrUX data is planned for removal from this API:  
   https://developers.google.com/speed/docs/insights/v5/get-started

4. **PageSpeed Insights v5 discovery document**  
   Machine-readable API description used to verify contract changes:  
   https://pagespeedonline.googleapis.com/$discovery/rest?version=v5

5. **PageSpeed Insights release notes**  
   Review before changing fixtures:  
   https://developers.google.com/speed/docs/insights/release_notes

6. **Google Cloud API key management and restrictions**  
   Key creation, API restrictions, storage, and rotation guidance:  
   https://cloud.google.com/docs/authentication/api-keys

7. **Lighthouse overview and integration guidance**  
   Lighthouse categories, reports, failed audits, automation, and integration context:  
   https://developer.chrome.com/docs/lighthouse/overview

8. **Lighthouse structured-data manual audit**  
   Confirms that manual SEO audits are distinct and do not affect the automated SEO score:  
   https://developer.chrome.com/docs/lighthouse/seo/structured-data/

9. **Lighthouse SEO audit examples**  
   Useful for interpreting stable audit IDs and remediation text:  
   https://developer.chrome.com/docs/lighthouse/seo/hreflang/  
   https://developer.chrome.com/docs/lighthouse/seo/link-text/

---

## 24. Final recommendation

Implement V1 as a separate `PageAudits` subsystem using the official `runPagespeed` API, a provider-neutral application contract, normalized PostgreSQL persistence, and a dedicated Hangfire worker.

Do not scrape the PageSpeed website, do not retain the full Lighthouse response, do not model CrUX data in this feature, and do not force PageSpeed execution into the existing HTTP/SSL logical-check pipeline.

Ship score, audit details, history, and the score delta. Regression buckets, alert thresholds, and incident integration wait for real run data.
