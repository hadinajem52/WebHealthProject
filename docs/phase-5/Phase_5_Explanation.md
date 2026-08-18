# Phase 5 Explained

## 1. What is Phase 5?

Phase 5 is the part of the project that turns monitoring data into useful operational information.

Before Phase 5, the application can already:

- manage clients, websites, environments, and endpoints;
- run safe HTTP checks;
- record check history;
- decide when an endpoint is healthy or unhealthy;
- create and manage incidents; and
- send incident notifications.

Phase 5 adds two important capabilities:

1. **SSL certificate monitoring** — checking whether HTTPS certificates are valid and close to expiring.
2. **Dashboard and reporting** — showing current health, trends, incidents, performance, and SSL information in one place, with CSV export.

In simple terms:

> Earlier phases collect and process monitoring evidence. Phase 5 makes that evidence useful to a person operating the system.

The formal Phase 5 goal is:

> Deliver operational visibility, SSL monitoring, and consistent reporting.

Phase 5 is estimated to take **8–12 working days** and primarily contributes to:

- **AC-06:** SSL expiry and severity boundaries.
- **AC-11:** Dashboard filters and CSV export use the same logical data.

---

## 2. Where Phase 5 fits in the project

The project is built in layers. Each phase provides something that the next phase needs.

```text
Phase 1: Application foundation
    ↓
Phase 2: Secure registry and authorization
    ↓
Phase 3: Scheduled HTTP monitoring and history
    ↓
Phase 4: Health, incidents, maintenance, and email
    ↓
Phase 5: SSL monitoring, dashboard, trends, reports, and CSV
```

Phase 5 is not a separate application. It extends the existing application and reuses the previous phases' services, data, authorization, scheduling, and incident behavior.

---

## 3. What the previous phases provide

### Phase 1 — Application foundation

Phase 1 provides the technical foundation:

- the ASP.NET Core application;
- PostgreSQL and Entity Framework Core;
- migrations;
- logging and correlation IDs;
- health endpoints;
- unit and integration test projects; and
- the shared Purity UI layout and styling.

Phase 5 depends on this foundation to store certificate data, query reports, display pages, and run tests.

### Phase 2 — Identity, authorization, registry, and audit

Phase 2 provides the things that Phase 5 reports about:

- clients;
- websites;
- environments;
- endpoints;
- owners and assignments;
- roles and permissions; and
- audit history.

It also provides the security rules that Phase 5 must continue to follow.

For example, if a user can only see endpoints assigned to their team, that same restriction must apply to:

- the dashboard;
- charts;
- reports; and
- CSV exports.

The dashboard must not show more data simply because the user opened a different URL or downloaded a file.

### Phase 3 — Scheduling and monitoring engine

Phase 3 provides the monitoring machinery:

- scheduled checks;
- manual checks;
- logical check IDs;
- leases that prevent duplicate work;
- retries and restart recovery;
- safe outbound HTTP requests;
- redirect and DNS-rebinding protection; and
- normalized check results.

Phase 5 reuses this machinery for SSL checks. SSL monitoring is added as another monitor type on the existing pipeline rather than being built as a completely separate scheduler.

That means SSL checks receive the same protections as HTTP checks, including scheduling, leases, idempotency, retry handling, and restart recovery.

### Phase 4 — Health, incidents, maintenance, and email

Phase 4 provides the business meaning of monitoring results:

- when failures are confirmed;
- when an incident opens;
- when an incident recovers;
- how incidents are deduplicated;
- how maintenance affects notifications; and
- how notifications are delivered.

Phase 5 uses this incident system for SSL expiry and certificate failures. It should not invent a second incident system.

Phase 5 also needs to display the incident information created in Phase 4, such as:

- open incidents;
- severity;
- owner;
- current state; and
- incident history.

---

## 4. The main work in Phase 5

Phase 5 is divided into seven implementation increments.

| Increment | Purpose | Current status |
|---|---|---|
| 5.1 | Capture certificate information safely | Complete |
| 5.2 | Add SSL monitor persistence and scheduling | Complete |
| 5.3 | Add SSL severity, deduplication, and renewal behavior | Planned |
| 5.4 | Implement response-time and page-size rules | Planned |
| 5.5 | Build shared reporting queries and CSV export | Planned |
| 5.6 | Build dashboard, trends, and reports UI | Planned |
| 5.7 | Verify query plans, performance, and the Phase 5 gate | Planned |

The individual increments 5.1 and 5.2 are documented in:

- [`Certificate_Capture_and_Safe_Tls_Inspection.md`](Certificate_Capture_and_Safe_Tls_Inspection.md)
- [`Ssl_Monitor_Type_and_Scheduling.md`](Ssl_Monitor_Type_and_Scheduling.md)

The overall Phase 5 gate is tracked in [`../General/Phased_Implementation_Plan.md`](../General/Phased_Implementation_Plan.md).

---

## 5. SSL monitoring

### 5.1 What SSL monitoring checks

For an HTTPS endpoint, the application checks the certificate presented for the requested hostname.

It records safe certificate information such as:

- subject;
- issuer;
- serial number;
- SHA-256 fingerprint;
- valid-from date;
- valid-to date;
- days remaining;
- hostname validation result; and
- trust or handshake result.

Private keys and complete encoded certificate files are not stored.

For an HTTP-only endpoint, SSL status is **Not Applicable** because HTTP does not use a certificate.

### 5.2 What has already been built

Increment 5.1 added safe certificate capture. It supports two separate situations:

1. A normally successful HTTPS request can expose the certificate that was accepted by normal platform TLS validation.
2. A dedicated SSL probe can observe an invalid certificate so that the system can report why it was rejected.

The dedicated probe always rejects the certificate. It does not weaken normal HTTP TLS validation and cannot be configured to accept invalid certificates.

Increment 5.2 then connected SSL checks to the existing monitoring pipeline:

- HTTPS endpoints receive an SSL monitor.
- HTTP endpoints do not receive an SSL monitor.
- SSL checks run daily by default.
- A TLS-related HTTP failure can request an urgent SSL check.
- SSL results are stored in the shared check-result system.
- SSL checks do not count toward uptime.

### 5.3 What is still needed for SSL

The remaining SSL work is increment 5.3:

#### Expiry severity

The application must apply the exact expiry rules:

- warning at 30 days;
- high severity at 15 days;
- critical at 7 days;
- expired certificates are critical; and
- expired, not-yet-valid, hostname-mismatched, untrusted, and handshake-failing certificates are critical.

The exact boundary tests matter. A certificate at exactly 30, 15, or 7 days must receive the intended severity rather than an adjacent severity.

#### Duplicate incident prevention

Repeated daily checks must not create a new incident every day for the same certificate.

The system uses the certificate fingerprint to identify the current certificate. It should maintain one active expiry incident for each endpoint and current fingerprint.

#### Renewal detection

When a certificate is replaced, its fingerprint changes. The system must:

1. recognize that the certificate is new;
2. record the renewal event;
3. evaluate the new certificate rather than the old one; and
4. resolve the old expiry incident after the renewal is confirmed.

---

## 6. Dashboard and reporting

The dashboard is the user-facing result of the earlier monitoring work.

It should help a user answer questions such as:

- Which endpoints are unhealthy right now?
- Which endpoints have open incidents?
- Which certificates will expire soon?
- What is the current response time?
- How has uptime changed over time?
- Which owner is responsible for an endpoint?
- What data is included in this report?

### 6.1 Dashboard content

Phase 5 should add:

- summary cards for endpoint health;
- a current-health table;
- client, website, environment, and owner information;
- response-time information;
- SSL status and days remaining;
- open incident information;
- uptime trends; and
- response-time trends.

The UI should follow the existing Purity UI Dashboard style and remain responsive on desktop and mobile screens.

### 6.2 Current health versus trends

The dashboard uses two different types of information:

#### Current health

Current health uses the latest confirmed state.

Example:

```text
An endpoint failed yesterday but recovered today.

Current health: Healthy
```

#### Trends

Trend charts keep historical eligible samples.

Example:

```text
Current health: Healthy
The chart still shows yesterday's failure.
```

This is important because recovery should make the endpoint look healthy now without erasing evidence that an outage happened.

### 6.3 Uptime calculations

Uptime must use eligible availability checks, not every kind of check.

In particular:

- SSL checks do not count toward uptime.
- Failed or unknown checks are handled according to the reporting rules.
- Manual checks are excluded from contractual uptime by default.
- Maintenance-suppressed checks remain visible but are excluded from contractual uptime by default.
- Reporting windows use a `[start, end)` boundary so records are not counted twice.

### 6.4 Response-time calculations

Phase 5 must calculate response-time percentiles from successful eligible HTTP samples.

- **P50** means the median response time.
- **P95** means the response time at or below which approximately 95% of eligible samples fall.

Failed checks must be shown separately. A timeout must not be treated as a normal response time such as 30,000 milliseconds and mixed into the percentile calculation.

### 6.5 Shared filters and CSV export

The dashboard and CSV export must use the same filtering and authorization rules.

For example, if the user selects:

```text
Environment: Production
Owner: Team A
Period: Last 30 days
```

the screen and downloaded CSV must represent the same logical set of endpoints and measurements.

The CSV must also provide:

- UTF-8 encoding;
- stable column names;
- ISO-8601 timestamps;
- correct quoting for commas and newlines;
- safe Unicode handling; and
- protection against spreadsheet formula injection.

This consistency is the purpose of **AC-11**.

---

## 7. Performance rules included in Phase 5

Phase 5 also adds the basic performance-monitoring behavior required by **BR-P01 through BR-P05**.

| Rule | Meaning |
|---|---|
| BR-P01 | Measure total response time and TTFB consistently in milliseconds. |
| BR-P02 | Use 1,500 ms warning and 3,000 ms critical defaults, with endpoint overrides. |
| BR-P03 | Open a slow-response incident only after three consecutive breaches by default. |
| BR-P04 | Warn about large pages using transferred content length when available; the default HTML threshold is 2 MB. |
| BR-P05 | Warn when performance samples are not comparable because their source or configuration differs. |

These rules make performance information more trustworthy. For example, a response measured from one monitoring location should not be presented as directly comparable to a response measured from a different location without an explanation.

---

## 8. How Phase 5 works with the earlier data flow

The overall flow becomes:

```text
Registry and endpoint configuration
        │
        ├── HTTP monitor ──> Safe HTTP check ──> Availability result
        │                                      │
        │                                      └── Health and incident processing
        │
        └── SSL monitor ──> Certificate check ──> SSL result
                                               │
                                               └── Health and incident processing

HTTP results + SSL results + incidents + endpoint ownership
        │
        └── Authorized reporting queries
                    │
                    ├── Dashboard cards and tables
                    ├── Chart.js trend data
                    ├── Reports
                    └── CSV export
```

The important point is that Phase 5 does not bypass the earlier business rules:

- Phase 2 authorization controls what the user may see.
- Phase 3 scheduling and safe transport control how checks run.
- Phase 4 health and incident logic controls how failures become incidents.
- Phase 5 presents the resulting information consistently.

---

## 9. What Phase 5 does not include

The following work belongs to later phases:

- recurring maintenance and daylight-saving handling — Phase 6;
- SEO checks — Phase 6;
- bounded crawling and broken-link analysis — Phase 6;
- retention, aggregates, and operational hardening — Phase 7;
- production deployment and project closeout — Phase 8.

Phase 5 may create a daily aggregate only if query-plan or reporting evidence shows that it is needed. The general daily aggregate and retention design belongs to Phase 7.

---

## 10. Definition of done for Phase 5

Phase 5 is complete when:

- SSL certificate details are stored and displayed safely.
- HTTPS certificates receive the correct severity at exactly 30, 15, and 7 days.
- Repeated checks do not create duplicate certificate incidents.
- Certificate renewal is detected and handled correctly.
- HTTP-only endpoints show SSL as Not Applicable.
- Response-time and page-size rules are implemented and tested.
- The dashboard shows current health, incidents, SSL, uptime, and trends.
- P50 and P95 use only eligible successful HTTP samples.
- Dashboard and CSV data use the same authorized query rules.
- CSV output is safe, stable, UTF-8, and timestamped clearly.
- Pages, charts, reports, and exports enforce authorization server-side.
- The UI passes keyboard, focus, label, contrast, and non-color status checks.
- Database migrations and indexes are verified.
- Representative query plans and dashboard P95 performance are recorded.
- SSL and dashboard workflows are demonstrated with automated test evidence.

The Phase 5 completion checklist is maintained in [`../General/Phased_Implementation_Plan.md`](../General/Phased_Implementation_Plan.md).
