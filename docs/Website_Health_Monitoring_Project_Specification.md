**INTERNSHIP PROJECT**

**Website Health &  
Monitoring Dashboard**

Business Requirements, Rules, Workflows and Acceptance Criteria

**Purpose:** Build a secure, demonstrable personal website-monitoring application that gives the intern structured exposure to professional .NET engineering. It is not enterprise-grade or production-certified.

| **Document information** | **Value**                                              |
| ------------------------ | ------------------------------------------------------ |
| Document owner           | Intern                                                   |
| Prepared for             | Personal internship/portfolio project                    |
| Version                  | 1.0                                                    |
| Date                     | 12 August 2026                                         |
| Status                   | Intern-owned baseline for implementation planning      |

**PERSONAL INTERNSHIP / PORTFOLIO PROJECT**

# Document Control

| **Version** | **Date**    | **Owner**        | **Description**                         |
| ----------- | ----------- | ---------------- | --------------------------------------- |
| 1.0         | 12 Aug 2026 | Intern | Initial business specification |
| 1.1         | 13 Aug 2026 | Intern | Clarified personal ownership and non-enterprise scope |

## How to Use This Document

- The intern uses Sections 1-15 as the authoritative business and functional baseline.
- The intern owns architecture, security, code quality, and deployment decisions; optional mentor or peer feedback is advisory.
- Decisions that change a business rule are recorded in the change log before implementation.
- Rules marked configurable have the stated value as their default, not as a hard-coded constant.

## Contents

- 1\. Executive Summary
- 2\. Business Context and Objectives
- 3\. Scope
- 4\. Stakeholders and Roles
- 5\. Key Concepts and Status Model
- 6\. Functional Requirements
- 7\. Detailed Business Rules
- 8\. Monitoring Workflows
- 9\. Data Requirements
- 10\. Notifications and Escalation
- 11\. Reporting and Dashboards
- 12\. Non-Functional Requirements
- 13\. Acceptance Criteria
- 14\. Internship Delivery Plan
- 15\. Testing Strategy
- 16\. Risks and Assumptions
- 17\. Future Enhancements
- Appendix A. Default Configuration
- Appendix B. Glossary

# 1\. Executive Summary

The Website Health & Monitoring Dashboard is an internal web application that continuously checks production and non-production websites managed by the company. It provides a single operational view of availability, response time, SSL certificate health, redirects, essential SEO configuration, broken links and selected performance indicators. When an issue is confirmed, the application opens an incident and notifies the responsible team according to configurable rules.

The project is a personal internship/portfolio application designed to demonstrate realistic monitoring workflows and sound engineering judgment. It provides practical experience in ASP.NET Core, Entity Framework Core, PostgreSQL, HTTP, background jobs, security, testing, observability, and deployment concepts without claiming enterprise or production certification.

**MVP outcome:** A secured dashboard that manages clients and websites, runs scheduled HTTP and SSL checks, records history, opens and resolves incidents, and sends deduplicated email alerts.

# 2\. Business Context and Objectives

## 2.1 Problem Statement

Development and support teams often discover website failures, expiring certificates, accidental noindex settings, broken redirects or degraded response times through manual review or client reports. These checks are inconsistent, difficult to audit and expensive to repeat across many websites.

## 2.2 Business Objectives

- Detect website availability issues before or shortly after users report them.
- Centralize website ownership, environments, check history and incidents.
- Reduce alert noise through confirmation, deduplication and recovery rules.
- Identify preventable SSL, redirect, SEO and broken-link risks.
- Provide evidence-based health reports for development and support management.
- Create a maintainable intern project that can be extended by the internal team.

## 2.3 Success Measures

| **Measure**           | **MVP target**                                                                          |
| --------------------- | --------------------------------------------------------------------------------------- |
| Availability coverage | At least 95% of enabled endpoints checked within their configured schedule              |
| Detection time        | Confirmed outage incident created within two check intervals                            |
| Alert duplication     | No duplicate opening alert for the same active incident and channel                     |
| History traceability  | Every check, incident status change and configuration change is timestamped             |
| Dashboard usability   | A support user can identify unhealthy websites and assigned owners in under two minutes |
| Automated tests       | Core monitoring and incident rules covered by unit/integration tests                    |

# 3\. Scope

## 3.1 In Scope

- Authentication and role-based access.
- Client, website, environment and endpoint management.
- Scheduled HTTP availability and response-time monitoring.
- SSL certificate expiry monitoring.
- Redirect validation, including loop and excessive-chain detection.
- Basic on-page SEO and technical configuration checks.
- Controlled internal-link crawling and broken-link reporting.
- Incident creation, acknowledgement, assignment, recovery and closure.
- Email notifications with throttling and escalation.
- Operational dashboard, history, reports and CSV export.
- Audit logging, retention configuration and health diagnostics.

## 3.2 Out of Scope for the MVP

- Full synthetic browser journeys such as login, checkout or form submission.
- External status-page publication for clients.
- Automatic remediation or production configuration changes.
- Vulnerability scanning, penetration testing or malware detection.
- Full Lighthouse/PageSpeed laboratory auditing.
- Native mobile application.
- Billing, SLA penalties or client invoicing.

## 3.3 Recommended Technology Baseline

| **Layer**            | **Recommended choice**                                            |
| -------------------- | ----------------------------------------------------------------- |
| Web application      | ASP.NET Core 10 MVC or Razor Pages                                |
| Language             | C#                                                                |
| Persistence          | Entity Framework Core with SQL Server                             |
| Authentication       | ASP.NET Core Identity                                             |
| Background execution | Hangfire or a hosted Worker Service                               |
| UI                   | Bootstrap or the company's Metronic template; Chart.js for charts |
| Logging              | Microsoft.Extensions.Logging with structured sink                 |
| Testing              | xUnit, FluentAssertions and an integration-test host              |

# 4\. Application Personas and Project Ownership

The intern is the sole project owner. The roles below are application personas used to implement and test authorization; they are not a project staffing model.

| **Role**                 | **Primary responsibilities**                                                               |
| ------------------------ | ------------------------------------------------------------------------------------------ |
| System Administrator     | Manages users, roles, global defaults, notification channels, retention and system status. |
| Operations Manager       | Views all clients, assigns responsibility, manages incidents, reports and escalations.     |
| Developer / Support User | Views assigned websites, acknowledges incidents, adds notes and resolves confirmed issues. |
| Read-Only Viewer         | Views permitted dashboards, incidents and reports without changing operational data.       |
| Background Worker        | Executes due checks, records results, evaluates rules and queues notifications.            |
| Optional Mentor / Peer   | May provide advisory feedback; has no ownership or required approval role.                  |

## 4.1 Permission Matrix

| **Capability**              | **Admin** | **Ops** | **Developer**   | **Viewer** |
| --------------------------- | --------- | ------- | --------------- | ---------- |
| Manage users and roles      | Yes       | No      | No              | No         |
| Manage clients/websites     | Yes       | Yes     | Assigned only\* | No         |
| Run check on demand         | Yes       | Yes     | Assigned        | No         |
| Acknowledge/assign incident | Yes       | Yes     | Assigned        | No         |
| Resolve/close incident      | Yes       | Yes     | Assigned        | No         |
| View reports                | All       | All     | Assigned        | Permitted  |
| Change global settings      | Yes       | No      | No              | No         |

\*Website configuration by developers is disabled by default and may be enabled through a role permission.

# 5\. Key Concepts and Status Model

| **Concept**        | **Definition**                                                                          |
| ------------------ | --------------------------------------------------------------------------------------- |
| Client             | The organization for which one or more websites are managed.                            |
| Website            | A logical web property such as the corporate site or portal.                            |
| Environment        | Production, staging, preproduction, test, development or custom.                        |
| Endpoint           | A specific URL monitored with its own schedule and validation rules.                    |
| Check              | One execution of one monitor type against one endpoint.                                 |
| Finding            | A rule violation discovered during a check, such as missing canonical or slow response. |
| Incident           | A tracked operational issue created after a finding meets confirmation rules.           |
| Maintenance window | An approved period during which alerts are suppressed for selected targets.             |

## 5.1 Endpoint Health Status

| **Status**  | **Meaning**                                                                                             |
| ----------- | ------------------------------------------------------------------------------------------------------- |
| Healthy     | Latest confirmed result passes all critical rules; warnings may be absent.                              |
| Warning     | Endpoint is reachable but one or more non-critical thresholds or configuration rules fail.              |
| Critical    | A confirmed availability, SSL, redirect or other critical rule has failed.                              |
| Unknown     | No completed result exists, monitoring is paused, or recent checks are inconclusive.                    |
| Maintenance | Endpoint is inside an active maintenance window; results are recorded but notifications are suppressed. |
| Disabled    | Monitoring has been explicitly disabled.                                                                |

## 5.2 Incident Lifecycle

Incidents follow the controlled state sequence below:

1. Open - created automatically or manually and awaiting ownership.
2. Acknowledged - a user has accepted responsibility for investigation.
3. In Progress - investigation or remediation is underway.
4. Monitoring Recovery - checks are passing but the recovery confirmation threshold is not yet met.
5. Resolved - recovery is confirmed or the issue has otherwise ended; a resolution note is required.
6. Closed - operational review is complete. A closed incident is immutable except for administrator reopening.

# 6\. Functional Requirements

| **ID** | **Requirement**                                                                      |
| ------ | ------------------------------------------------------------------------------------ |
| FR-001 | Authenticate users and enforce role-based authorization on every protected action.   |
| FR-002 | Create and maintain clients, websites, environments and endpoints.                   |
| FR-003 | Schedule and execute monitor checks without blocking web requests.                   |
| FR-004 | Capture HTTP status, response time, redirect path, payload size and failure details. |
| FR-005 | Inspect SSL certificate validity and remaining days.                                 |
| FR-006 | Evaluate configured SEO and technical rules.                                         |
| FR-007 | Crawl permitted internal pages and report broken or redirected links.                |
| FR-008 | Create and manage incidents based on confirmation and recovery rules.                |
| FR-009 | Notify recipients by email with suppression, deduplication and escalation.           |
| FR-010 | Display current health, trends, incidents and upcoming certificate expiry.           |
| FR-011 | Export filtered operational data to CSV.                                             |
| FR-012 | Record audit events for material changes and user actions.                           |
| FR-013 | Provide diagnostics for worker, queue, database and notification health.             |

# 7\. Detailed Business Rules

All rules below are mandatory unless explicitly marked optional or deferred. Configurable values use the defaults in Appendix A.

## 7.1 Identity and Access

| **ID** | **Business rule**                                                                                                               | **Verification / expected result**                                                           |
| ------ | ------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| BR-A01 | Every interactive user must authenticate before accessing operational data.                                                     | Unauthenticated requests are redirected to login or receive HTTP 401/403 for APIs.           |
| BR-A02 | Authorization is evaluated server-side; hidden UI controls do not count as access control.                                      | A direct request by an unauthorized user is rejected and audited.                            |
| BR-A03 | Only administrators may create users, assign roles, disable accounts or reset another user's access.                            | Non-admin attempts fail with no data change.                                                 |
| BR-A04 | A disabled user cannot sign in and existing sessions are invalidated at the next security-stamp check.                          | The user loses access without deleting historical ownership records.                         |
| BR-A05 | Passwords and lockout follow the application's configured Identity policy; passwords are never stored or logged in plain text. | Authentication storage contains password hashes only; sensitive fields are absent from logs. |
| BR-A06 | All create, update, delete, permission and incident-state actions record actor, timestamp and changed entity.                   | Audit history is queryable by date, user, action and entity.                                 |

## 7.2 Client, Website and Endpoint Management

| **ID** | **Business rule**                                                                                                       | **Verification / expected result**                                                             |
| ------ | ----------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| BR-W01 | Client name is required and unique after trimming, using case-insensitive comparison.                                   | Names such as 'Client A' and ' client a ' cannot coexist.                                      |
| BR-W02 | A website belongs to exactly one client and must have a unique name within that client.                                 | Duplicate names within a client are rejected; the same name may exist for another client.      |
| BR-W03 | Each website must have at least one environment before monitoring can be enabled.                                       | Enable action is blocked until an environment exists.                                          |
| BR-W04 | An endpoint URL must be an absolute HTTP or HTTPS URI; embedded credentials and unsupported schemes are rejected.       | Invalid, relative, file, FTP and credential-bearing URLs fail validation.                      |
| BR-W05 | Production endpoints must use HTTPS unless an administrator records an explicit exception reason.                       | HTTP production endpoint shows a critical configuration warning or saved exception.            |
| BR-W06 | Normalized endpoint URLs must be unique within an environment and monitor type.                                         | Equivalent URLs differing only by host casing or default port cannot duplicate the same check. |
| BR-W07 | Deleting an entity with monitoring history is a soft delete; history remains reportable.                                | The entity disappears from active lists but retains audit and historical relationships.        |
| BR-W08 | Disabling a website disables future scheduled checks for all its endpoints but does not erase due or completed history. | No new scheduled work is created after disablement.                                            |
| BR-W09 | Each endpoint has a responsible user or team, inherited from its website unless explicitly overridden.                  | Incident assignment uses the endpoint override first, then website owner.                      |
| BR-W10 | Tags may classify websites by technology, service, region or support group and are trimmed and de-duplicated.           | Repeated tags are stored once and filters return correct matches.                              |

## 7.3 Scheduling and Execution

| **ID** | **Business rule**                                                                                                                       | **Verification / expected result**                                          |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| BR-S01 | Only enabled endpoints under enabled websites are eligible for scheduled checks.                                                        | Disabled targets produce no new scheduled executions.                       |
| BR-S02 | Default HTTP interval is five minutes for production and fifteen minutes for non-production; administrators may configure per endpoint. | NextDueAt is calculated from the endpoint interval and last scheduled time. |
| BR-S03 | A check must not run concurrently with another active check of the same monitor type and endpoint.                                      | A distributed lock or equivalent prevents duplicate execution.              |
| BR-S04 | A check exceeding its timeout is recorded as Timeout, not left permanently running.                                                     | The execution closes with duration and normalized failure category.         |
| BR-S05 | Failed jobs may retry for infrastructure errors, but each logical check has one final result and retries are linked to it.              | History does not count worker retries as separate availability samples.     |
| BR-S06 | On-demand checks are allowed only for authorized users and are labeled Manual.                                                          | Manual results show initiator and do not shift the scheduled cadence.       |
| BR-S07 | The worker uses UTC for scheduling and storage; display uses the user's or company timezone.                                            | The same timestamp sorts consistently across daylight-saving changes.       |
| BR-S08 | A scheduler delay does not generate a backlog of every missed five-minute slot; it schedules one catch-up check then resumes normally.  | Recovery from downtime does not overload targets or the worker.             |

## 7.4 HTTP Availability and Redirects

| **ID** | **Business rule**                                                                                                                           | **Verification / expected result**                                                 |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| BR-H01 | An HTTP check records DNS/connect/TLS/TTFB/total duration when available, final status code, content length and redirect chain.             | A completed result exposes the captured metrics or a clear not-available value.    |
| BR-H02 | HTTP 200-299 is healthy by default; accepted status codes may be configured per endpoint.                                                   | A configured 204 endpoint is healthy; an unaccepted status raises a finding.       |
| BR-H03 | HTTP 300-399 is evaluated against redirect rules, not treated automatically as healthy.                                                     | The result records every hop and final target.                                     |
| BR-H04 | HTTP 400-499 is critical for public production pages except explicitly accepted codes; HTTP 500-599 is always critical.                     | Findings carry ClientError or ServerError severity as configured.                  |
| BR-H05 | DNS failure, connection refusal, TLS handshake failure and timeout are stored as distinct failure categories.                               | Dashboards and alerts display the normalized category and safe diagnostic message. |
| BR-H06 | Redirect chains are limited to ten hops by default; exceeding the limit is critical.                                                        | The check stops safely and reports ExcessiveRedirects.                             |
| BR-H07 | Revisiting a URL within one redirect chain is a redirect loop and is critical.                                                              | The repeated URL and chain are recorded without infinite execution.                |
| BR-H08 | A production HTTP URL that does not redirect to HTTPS is a warning or critical finding according to endpoint policy.                        | HTTPS enforcement is visible on the endpoint result.                               |
| BR-H09 | A successful response containing a configured required text marker is healthy only when the marker is found using the configured case rule. | A 200 response with missing marker raises ContentMismatch.                         |
| BR-H10 | Response bodies are size-limited and are not retained by default; only approved diagnostic snippets may be stored with secrets removed.     | Large or sensitive content is not persisted in check history.                      |

## 7.5 Confirmation, Health and Incidents

| **ID** | **Business rule**                                                                                                              | **Verification / expected result**                                                       |
| ------ | ------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------- |
| BR-I01 | A critical availability incident opens after two consecutive failed logical checks by default.                                 | One transient failure shows a pending warning; the second opens one incident.            |
| BR-I02 | A failure counter resets when a passing result occurs before the confirmation threshold.                                       | Fail-pass-fail does not open an incident when the threshold is two consecutive failures. |
| BR-I03 | Only one active incident may exist for the same endpoint, monitor type and normalized issue key.                               | Repeated failures update the active incident rather than creating duplicates.            |
| BR-I04 | A new, materially different issue may create a separate incident even while another incident is active.                        | SSL expiry and HTTP outage can be tracked independently.                                 |
| BR-I05 | Recovery requires two consecutive passing checks by default.                                                                   | The first pass changes the incident to Monitoring Recovery; the second resolves it.      |
| BR-I06 | An automatically resolved incident records recovery time, outage duration and the results that confirmed recovery.             | Incident history identifies both failure and recovery evidence.                          |
| BR-I07 | Manual resolution requires a resolution category and note; it does not falsify check history.                                  | The incident becomes Resolved with user, reason and note while failed results remain unchanged; closure is a separate lifecycle action. |
| BR-I08 | Acknowledgement, assignment, status change and notes append to the incident timeline and are never silently overwritten.       | Timeline ordering and authors are preserved.                                             |
| BR-I09 | Closing is allowed only from Resolved, except administrators may force close with an audit reason.                             | Invalid state transitions are rejected server-side.                                      |
| BR-I10 | Reoccurrence after closure opens a new incident linked to the previous incident when the issue key matches within thirty days. | Recurring history is visible without reopening an old immutable record.                  |

## 7.6 Uptime and Response-Time Calculations

| **ID** | **Business rule**                                                                                                                                   | **Verification / expected result**                                                 |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| BR-U01 | Uptime percentage equals healthy availability samples divided by eligible availability samples multiplied by 100.                                   | The calculation uses logical checks, not worker retry attempts.                    |
| BR-U02 | Manual checks, disabled periods and maintenance-suppressed checks are excluded from contractual uptime by default but remain visible operationally. | Report clearly shows included and excluded counts.                                 |
| BR-U03 | Unknown or cancelled checks are excluded from the denominator unless a report explicitly includes them.                                             | No-result samples do not automatically lower uptime.                               |
| BR-U04 | Reporting windows use \[start, end) boundaries in UTC to prevent double counting.                                                                   | A check exactly at the end appears in the next period only.                        |
| BR-U05 | Response-time percentiles use successful eligible HTTP samples; failed checks are reported separately.                                              | P50/P95 values do not treat timeouts as normal milliseconds.                       |
| BR-U06 | Dashboard health uses the latest confirmed state, while trend charts use all eligible samples.                                                      | A recovered endpoint appears healthy without erasing the prior outage from charts. |

## 7.7 SSL Certificate Monitoring

| **ID** | **Business rule**                                                                                                 | **Verification / expected result**                                            |
| ------ | ----------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| BR-C01 | SSL checks apply to HTTPS endpoints and inspect the certificate presented for the requested host.                 | HTTP-only endpoints show Not Applicable for certificate status.               |
| BR-C02 | The system records subject, issuer, serial fingerprint, valid-from, valid-to and days remaining when available.   | Certificate history supports renewal comparison without storing private keys. |
| BR-C03 | An expired, not-yet-valid, hostname-mismatched, untrusted or handshake-failing certificate is critical.           | The result identifies the validation category.                                |
| BR-C04 | Expiry severity defaults to warning at 30 days, high at 15 days and critical at 7 days.                           | Boundary dates produce the expected severity and alert.                       |
| BR-C05 | One active certificate-expiry incident is maintained per endpoint and current certificate fingerprint.            | Repeated daily checks do not create duplicate incidents.                      |
| BR-C06 | When a certificate fingerprint changes, expiry evaluation uses the new certificate and records the renewal event. | A renewed certificate resolves the old expiry incident after confirmation.    |
| BR-C07 | SSL checks run daily by default and immediately after a TLS-related HTTP failure when permitted.                  | Urgent validation occurs without waiting for the next daily schedule.         |

## 7.8 SEO and Technical Configuration

| **ID** | **Business rule**                                                                                                          | **Verification / expected result**                                           |
| ------ | -------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| BR-E01 | SEO checks run only on successful HTML responses and do not parse binary content.                                          | Non-HTML content is marked Not Applicable.                                   |
| BR-E02 | The page title is missing when no non-empty title element exists; duplicate title elements are a warning.                  | Finding distinguishes missing and duplicate title.                           |
| BR-E03 | A missing or empty meta description is a warning unless the endpoint disables this rule.                                   | Endpoint policy controls applicability.                                      |
| BR-E04 | A canonical URL must be absolute, valid and unique; unexpected cross-domain canonical is high severity on production.      | Canonical value and expected host comparison are recorded.                   |
| BR-E05 | A production page containing noindex is high severity unless explicitly expected for that endpoint.                        | Expected-noindex pages pass; accidental noindex raises a finding.            |
| BR-E06 | robots.txt is evaluated at the origin root, not relative to a nested endpoint path.                                        | For <https://host/a/b>, the checked robots URL is <https://host/robots.txt>. |
| BR-E07 | A production robots.txt containing a user-agent wildcard with Disallow: / is critical unless an approved exception exists. | The parser respects group structure and ignores comments.                    |
| BR-E08 | sitemap availability may be checked from configured URLs and sitemap directives in robots.txt.                             | Missing required sitemap produces a warning; invalid status is recorded.     |
| BR-E09 | Non-production environments may require noindex and restrictive robots rules; their policy is the reverse of production.   | A publicly indexable staging endpoint raises a warning or critical finding.  |
| BR-E10 | SEO results retain extracted values needed for diagnosis but not the entire HTML document.                                 | History shows title/canonical/robots decisions without storing full pages.   |

## 7.9 Broken-Link Crawler

| **ID** | **Business rule**                                                                                                      | **Verification / expected result**                                |
| ------ | ---------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| BR-L01 | A crawl starts only from configured seed URLs and stays within allowed hostnames and path prefixes.                    | External and disallowed targets are not recursively crawled.      |
| BR-L02 | robots.txt is respected by default; an authorized administrator may override it only for owned non-production targets. | Production crawls do not bypass published crawl restrictions.     |
| BR-L03 | The crawler normalizes URLs, removes fragments and avoids revisiting the same normalized URL in one run.               | Anchor variations do not create duplicate pages.                  |
| BR-L04 | Query-string handling is configurable; tracking parameters are ignored by default to prevent URL explosion.            | utm_\* variations consolidate to one crawl target.                |
| BR-L05 | Maximum pages, depth, concurrency, requests per second and run duration are enforced.                                  | A crawl stops gracefully and reports its limiting reason.         |
| BR-L06 | Links are classified as healthy, redirected, broken, blocked, timeout, skipped or unknown.                             | The report includes source page, link target and observed result. |
| BR-L07 | A broken internal link is reported once per source-target pair per crawl while aggregated counts show affected pages.  | Duplicates do not inflate unique broken-link totals.              |
| BR-L08 | External links may be checked with lower concurrency but are not recursively crawled.                                  | External target status is captured without exploring its site.    |
| BR-L09 | Crawls identify themselves with a configurable user-agent and include a contact identifier.                            | Requests are distinguishable in client logs.                      |
| BR-L10 | Crawl cancellation preserves completed findings and marks the run Cancelled.                                           | Partial results are visible and never labeled complete.           |

## 7.10 Performance Monitoring

| **ID** | **Business rule**                                                                                                                  | **Verification / expected result**                                 |
| ------ | ---------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| BR-P01 | Total response time and TTFB are measured consistently using the monitoring client and stored in milliseconds.                     | Metrics include measurement timestamps and missing-value handling. |
| BR-P02 | Default warning threshold is 1,500 ms and critical threshold is 3,000 ms for total response time; endpoint overrides are allowed.  | Boundary and override tests produce correct severity.              |
| BR-P03 | A slow-response incident opens only after three consecutive threshold breaches by default.                                         | One isolated slow response creates no incident.                    |
| BR-P04 | Page-size warnings use transferred content length where available, with a default HTML threshold of 2 MB.                          | Compressed/uncompressed measurement type is labeled clearly.       |
| BR-P05 | Performance comparisons use similar monitor location and configuration; otherwise the UI warns that results may not be comparable. | Reports display the monitor source and relevant configuration.     |

## 7.11 Maintenance Windows

| **ID** | **Business rule**                                                                                                                | **Verification / expected result**                                                           |
| ------ | -------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| BR-M01 | A maintenance window has target scope, start, end, timezone, reason and creator; end must be after start.                        | Invalid windows are rejected.                                                                |
| BR-M02 | Checks continue during maintenance by default, but new notifications are suppressed and results are marked Maintenance.          | Operational evidence is retained without alert noise.                                        |
| BR-M03 | An incident already open before maintenance remains open; escalation pauses unless an administrator chooses otherwise.           | Incident age and paused escalation time are distinguishable.                                 |
| BR-M04 | At maintenance end, the next failed check resumes normal confirmation from zero unless configured to continue the prior counter. | The default prevents a single post-maintenance failure from immediately opening an incident. |
| BR-M05 | Recurring windows are expanded into concrete occurrences and use timezone-aware scheduling.                                      | Daylight-saving transitions do not create ambiguous occurrences.                             |

## 7.12 Notifications and Escalation

| **ID** | **Business rule**                                                                                                  | **Verification / expected result**                                              |
| ------ | ------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------- |
| BR-N01 | An opening notification is sent only when an incident is created, not for every failed check.                      | One active incident produces one opening message per enabled channel.           |
| BR-N02 | Recipients are resolved from endpoint owner, website owner, client support group and configured escalation policy. | The notification audit lists resolved recipients.                               |
| BR-N03 | The same notification event, recipient and channel is idempotent.                                                  | Worker retry cannot send a duplicate message.                                   |
| BR-N04 | Reminder frequency defaults to 60 minutes for unacknowledged critical incidents and is configurable.               | Acknowledgement stops unacknowledged reminders.                                 |
| BR-N05 | Escalation occurs after 30 minutes unacknowledged by default and moves to the next configured level.               | Escalation timestamps and recipients appear in the timeline.                    |
| BR-N06 | Recovery notification is sent after recovery confirmation, including duration and a link to the incident.          | No recovery message is sent after only one passing check when two are required. |
| BR-N07 | Notification failure does not roll back the check or incident; it is retried and exposed in system diagnostics.    | Incident remains valid even when email is unavailable.                          |
| BR-N08 | Messages must not include secrets, full response bodies or unsafe exception details.                               | Templates use approved diagnostic fields only.                                  |

## 7.13 Reporting, Audit and Retention

| **ID** | **Business rule**                                                                                                     | **Verification / expected result**                                             |
| ------ | --------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| BR-R01 | Dashboard totals use the latest visible status per enabled endpoint and disclose the selected filters and as-of time. | Changing filters recomputes every card consistently.                           |
| BR-R02 | Reports support client, website, environment, owner, status, monitor type and date filters.                           | Exports use the same filtered dataset as the screen.                           |
| BR-R03 | CSV exports use UTF-8, stable column names and ISO-8601 timestamps.                                                   | Arabic and other Unicode values open correctly and timestamps are unambiguous. |
| BR-R04 | Audit events are append-only for normal users and record before/after values for material configuration changes.      | An edited endpoint can be reconstructed from audit history.                    |
| BR-R05 | Raw check results are retained for 90 days by default; daily aggregates and incidents are retained for 24 months.     | Retention job deletes or aggregates only eligible records.                     |
| BR-R06 | Retention never deletes records under an active legal or operational hold.                                            | Held entities survive the retention job and the action is logged.              |
| BR-R07 | Soft-deleted configuration remains available to historical reports but is excluded from active operational counts.    | Old reports resolve names without reactivating targets.                        |

## 7.14 Security and Safe Monitoring

| **ID** | **Business rule**                                                                                                             | **Verification / expected result**                                                |
| ------ | ----------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| BR-Q01 | The monitoring engine blocks loopback, link-local, metadata-service and unauthorized private-network destinations by default. | SSRF validation rejects prohibited resolved IP ranges, including after redirects. |
| BR-Q02 | DNS is revalidated during redirects to prevent a permitted hostname from redirecting to a prohibited network.                 | Redirect destination policy is enforced at every hop.                             |
| BR-Q03 | Secrets such as SMTP credentials are stored using approved secret configuration and never in source control.                  | Repository and logs contain no operational secret values.                         |
| BR-Q04 | TLS validation is enabled; bypassing certificate validation is prohibited in production.                                      | Production configuration cannot silently accept invalid certificates.             |
| BR-Q05 | User-supplied labels and diagnostic content are output-encoded to prevent stored or reflected XSS.                            | Security tests with markup payloads render as text.                               |
| BR-Q06 | State-changing web requests require anti-forgery protection or equivalent API authorization controls.                         | Forged requests are rejected.                                                     |
| BR-Q07 | Monitoring uses bounded concurrency, timeouts and response-size limits to protect the platform and target sites.              | Load testing demonstrates enforcement of limits.                                  |

# 8\. Monitoring Workflows

## 8.1 Scheduled Availability Check

1. Scheduler selects due, enabled endpoints and creates logical check jobs.
2. Worker acquires an endpoint/monitor lock and validates the destination against network policy.
3. Worker executes the request using the configured timeout, redirect and header rules.
4. Result is normalized and stored with metrics, status and safe diagnostics.
5. Rule engine updates counters, endpoint health and any active incident.
6. Notification events are queued when an incident opens, escalates or recovers.
7. Lock is released and the next due time is calculated.

## 8.2 Failure and Recovery

| **Event**                  | **System behavior**                                                                      |
| -------------------------- | ---------------------------------------------------------------------------------------- |
| First critical failure     | Record failure; show pending confirmation; do not open incident when threshold is two.   |
| Second consecutive failure | Open incident, assign owner, set endpoint Critical and queue opening alert.              |
| Further failures           | Append evidence and update last-seen time; do not create duplicate incident.             |
| First passing check        | Set incident to Monitoring Recovery; keep endpoint warning/critical according to policy. |
| Second passing check       | Resolve incident, calculate duration, set endpoint Healthy and queue recovery alert.     |

## 8.3 On-Demand Check

1. Authorized user selects Run Now and sees that the request was queued.
2. Manual check respects all network, timeout and safety rules.
3. Result is labeled Manual and linked to the initiating user.
4. Manual checks can add evidence to incidents but do not change scheduled uptime calculations by default.

## 8.4 Crawler Run

1. Validate seed URLs, allowed hosts, page limit, depth and crawl rate.
2. Fetch seed, parse supported links, normalize targets and enqueue eligible internal URLs.
3. Classify results and store source-target relationships.
4. Stop at configured limits or cancellation; mark the run Complete, Partial, Failed or Cancelled.
5. Compare with the previous completed crawl to show new, continuing and resolved broken links.

# 9\. Data Requirements

## 9.1 Core Entities

| **Entity**          | **Minimum business data**                                                                             |
| ------------------- | ----------------------------------------------------------------------------------------------------- |
| Client              | Id, name, active flag, support group, notes, audit fields                                             |
| Website             | Id, client, name, technology/CMS, owner, tags, active flag                                            |
| Environment         | Id, website, type, base URL, production flag, policy profile                                          |
| Endpoint            | Id, environment, URL, monitor type, interval, timeout, thresholds, owner, enabled flag                |
| CheckResult         | Logical check id, endpoint, type, source, timestamps, outcome, metrics, failure category, diagnostics |
| Finding             | Check result, rule key, severity, observed value, expected value, issue key                           |
| Incident            | Issue key, endpoint, severity, status, owner, opened/acknowledged/resolved/closed times, resolution   |
| IncidentEvent       | Incident, event type, actor, timestamp, note, old/new state                                           |
| MaintenanceWindow   | Scope, start/end, timezone, recurrence, reason, suppression policy                                    |
| Notification        | Incident event, channel, recipient, template, state, attempts, sent time, error category              |
| CrawlRun/LinkResult | Scope, limits, status, URL relationships, classification and timing                                   |
| AuditEvent          | Actor, action, entity, entity id, timestamp, before/after safe values                                 |

## 9.2 Data Integrity Rules

- Use database constraints for required relationships and uniqueness where practical.
- Use optimistic concurrency for user-edited configuration and incidents.
- Use UTC DateTimeOffset-compatible values for all persisted timestamps.
- Do not cascade-delete monitoring history when a client, website or endpoint is removed.
- Store normalized enums or lookup identifiers for status and failure categories.
- Large diagnostics must be bounded; sensitive headers and bodies must not be persisted.

# 10\. Notifications and Escalation

## 10.1 Email Content

| **Message type** | **Required content**                                                                             |
| ---------------- | ------------------------------------------------------------------------------------------------ |
| Incident opened  | Severity, client/site/environment, endpoint, issue, first/last failure, owner and dashboard link |
| Escalation       | Original incident details, unacknowledged duration, current level and required action            |
| Recovery         | Recovered endpoint, confirmation time, outage duration, result summary and incident link         |
| SSL warning      | Host, issuer, expiry date, days remaining, severity and responsible owner                        |
| Daily summary    | Healthy/warning/critical counts, open incidents, expiring SSL and new broken links               |

## 10.2 Notification Delivery States

- Pending
- Processing
- Sent
- Retry Scheduled
- Failed Permanently
- Suppressed

# 11\. Reporting and Dashboards

## 11.1 Main Dashboard

- Summary cards: monitored endpoints, healthy, warning, critical, unknown and maintenance.
- Current-health table with client, website, environment, response time, SSL days, open incident and owner.
- Response-time trend and uptime trend for the selected period.
- Open incidents by severity, age and acknowledgement state.
- Certificates expiring within 30, 15 and 7 days.
- New and continuing broken links from the latest crawl.
- System diagnostics: worker heartbeat, queue depth, last notification error and overdue checks.

## 11.2 Required Reports

| **Report**           | **Purpose**                                                                           |
| -------------------- | ------------------------------------------------------------------------------------- |
| Availability Summary | Uptime, failures, outage duration and eligible sample counts by endpoint.             |
| Incident Report      | Incident volume, severity, acknowledgement and resolution time, owner and recurrence. |
| SSL Expiry           | Current certificate details and expiry bands.                                         |
| Performance Trend    | P50/P95 response time, threshold breaches and slowest endpoints.                      |
| Broken Links         | Source page, target, classification, first seen, last seen and current state.         |
| SEO Configuration    | Latest title, description, canonical, indexing, robots and sitemap findings.          |
| Audit Report         | Who changed configuration or incident state, when, and what changed.                  |

# 12\. Non-Functional Requirements

| **ID** | **Requirement**                                                                                                                      |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| NFR-01 | The application must support at least 500 endpoints and 100 concurrent checks without duplicate execution in the target environment. |
| NFR-02 | Common dashboard views should load within three seconds at the 95th percentile for the target dataset.                               |
| NFR-03 | Monitoring and notification processing must continue independently of interactive web sessions.                                      |
| NFR-04 | All external calls must have explicit timeouts, cancellation and bounded response sizes.                                             |
| NFR-05 | The application must use structured logging and correlation identifiers across checks, incidents and notifications.                  |
| NFR-06 | The application must expose liveness and readiness health checks for web, worker, database and queue dependencies.                   |
| NFR-07 | Database migrations must be versioned and deployable through a controlled release process.                                           |
| NFR-08 | Configuration and secrets must be environment-specific; production secrets must not be stored in the repository.                     |
| NFR-09 | The UI should meet practical accessibility expectations: keyboard navigation, labels, contrast and non-color status cues.            |
| NFR-10 | Dates are displayed in the configured timezone while raw exports include ISO-8601 offsets.                                           |
| NFR-11 | The system must recover safely after restart without duplicating logical checks, incidents or notifications.                         |
| NFR-12 | Critical rule evaluation must be deterministic and covered by automated tests.                                                       |

# 13\. Acceptance Criteria

| **ID** | **Acceptance criterion**                                                                                            |
| ------ | ------------------------------------------------------------------------------------------------------------------- |
| AC-01  | Admin creates a client, website, production environment and endpoint; validation and uniqueness rules are enforced. |
| AC-02  | An enabled production endpoint is checked on schedule and history contains status, duration and timestamp.          |
| AC-03  | Two consecutive availability failures open exactly one critical incident and send one opening email.                |
| AC-04  | A passing result followed by another passing result resolves the incident and sends one recovery email.             |
| AC-05  | A redirect loop is detected and stored without hanging the worker.                                                  |
| AC-06  | An HTTPS endpoint displays certificate expiry and generates correct severity at 30/15/7-day boundaries.             |
| AC-07  | Production noindex and wildcard Disallow: / rules generate the expected findings.                                   |
| AC-08  | A crawl stays within scope, respects limits and reports a broken internal link with its source page.                |
| AC-09  | Maintenance suppresses notifications but retains marked check results.                                              |
| AC-10  | Unauthorized users cannot invoke protected actions using direct requests.                                           |
| AC-11  | Dashboard filters and CSV export return the same logical dataset.                                                   |
| AC-12  | An application restart does not create duplicate incidents or duplicate notification events.                        |
| AC-13  | Audit history records configuration and incident-state changes with actor and timestamp.                            |
| AC-14  | Retention removes expired raw results while preserving active incidents, holds and aggregates.                      |
| AC-15  | Automated unit and integration tests pass in the delivery pipeline.                                                 |

## 13.1 Definition of Done

- Code is reviewed and merged through the agreed branch process.
- Business rules and authorization are implemented server-side.
- Unit and integration tests cover success, boundary and failure paths.
- Database migration and seed/configuration instructions are included.
- No secrets, debug output or unsafe certificate bypasses are committed.
- Feature is demonstrated against controlled healthy and failing test endpoints.
- Technical and user documentation is updated.
- Deployment to the agreed test environment is successful and smoke-tested.

# 14\. Internship Delivery Plan

| **Phase**                 | **Suggested duration** | **Deliverable and review gate**                                                        |
| ------------------------- | ---------------------- | -------------------------------------------------------------------------------------- |
| 0\. Discovery and Design  | 3-4 days               | Wireframes, domain model, architecture note, backlog and development setup.            |
| 1\. Foundation            | 1 week                 | Identity, roles, client/site/environment/endpoint CRUD, validation and audit baseline. |
| 2\. Monitoring Engine     | 1-2 weeks              | Scheduler, worker, HTTP client, normalized results, history and tests.                 |
| 3\. Incidents and Alerts  | 1 week                 | Confirmation/recovery engine, incident workflow, email queue and deduplication.        |
| 4\. SSL and Dashboard     | 1 week                 | Certificate checks, status dashboard, trends and filters.                              |
| 5\. SEO and Crawler       | 1-2 weeks              | SEO rules, bounded crawler, broken-link comparison and reports.                        |
| 6\. Hardening and Release | 1 week                 | Security review, performance, retention, documentation, deployment and demo.           |

## 14.1 Weekly Mentor Review

- Demo working software, not only slides or screenshots.
- Review one architectural decision and its trade-offs.
- Review test evidence for newly implemented business rules.
- Identify blockers, risks and next-week goals.
- Select one pull request for detailed code-quality feedback.

## 14.2 Intern Evaluation Rubric

| **Area**                      | **Weight** | **Evidence**                                                         |
| ----------------------------- | ---------- | -------------------------------------------------------------------- |
| Functional correctness        | 30%        | Rules and acceptance criteria behave correctly.                      |
| Code quality and architecture | 20%        | Clear boundaries, naming, error handling and maintainability.        |
| Testing                       | 15%        | Meaningful automated coverage and reliable test design.              |
| Security and reliability      | 15%        | Authorization, SSRF controls, safe logging, retries and idempotency. |
| Communication                 | 10%        | Progress updates, questions, demos and documentation.                |
| Ownership and learning        | 10%        | Independent investigation, feedback adoption and sound judgment.     |

# 15\. Testing Strategy

## 15.1 Unit Tests

- URL normalization and validation.
- Status-code classification.
- Redirect loop and hop limit.
- Failure confirmation and recovery counters.
- Incident deduplication and valid state transitions.
- SSL day-boundary severity.
- robots.txt group parsing and noindex detection.
- Uptime inclusion/exclusion and period boundaries.
- Notification idempotency and escalation timing.

## 15.2 Integration Tests

- HTTP checks against an in-process test server returning controlled statuses, redirects, delays and HTML.
- Database persistence, concurrency and unique constraints.
- Background job execution and restart behavior.
- Authentication and authorization using test users for every role.
- Email queue using a fake transport that records deliveries.
- Crawler scope, limits, cancellation and source-target reporting.

## 15.3 Manual / UAT Scenarios

1. Create a client website and observe first successful scheduled check.
2. Simulate outage, confirm one incident and one alert, acknowledge it, then restore service.
3. Add maintenance, fail the endpoint and confirm result retention with suppressed alert.
4. Test an expiring or generated test certificate at threshold boundaries.
5. Crawl a controlled mini-site containing working, redirected and broken links.
6. Export a filtered report and compare row counts with the dashboard.

# 16\. Risks and Assumptions

| **Type**   | **Item**                                                | **Mitigation / decision**                                                                 |
| ---------- | ------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| Assumption | The platform monitors only websites owned by the intern or explicitly permitted for testing. | Require ownership/permission evidence during onboarding. |
| Assumption | Email is the MVP notification channel.                  | Design a channel interface for future Teams/Slack.                                        |
| Risk       | Monitoring traffic may burden client sites.             | Bound concurrency, rate and crawl frequency; use clear user-agent.                        |
| Risk       | False positives create alert fatigue.                   | Use confirmation, recovery and deduplication defaults with per-endpoint overrides.        |
| Risk       | SSRF exposes internal network resources.                | Enforce destination policy before request and at every redirect.                          |
| Risk       | Large history affects dashboard performance.            | Retention, aggregation, indexing and asynchronous export.                                 |
| Risk       | Scope grows beyond internship duration.                 | Treat HTTP/SSL/incidents/email as the protected MVP; defer crawler/performance if needed. |
| Decision   | Operational status is based on confirmed state.         | Retain raw results so teams can still inspect transient failures.                         |

# 17\. Future Enhancements

- Microsoft Teams, Slack and SMS notification channels.
- Browser-based synthetic journeys for login and critical forms.
- Google PageSpeed Insights or Lighthouse integration.
- Public or client-specific status pages.
- Azure DevOps/GitHub release correlation and maintenance automation.
- Anomaly detection for response time and incident patterns.
- Multi-region monitors to distinguish local network issues from global outages.
- AI-assisted incident summaries based only on approved stored diagnostics.

# Appendix A. Default Configuration

| **Setting**                  | **Default**            | **Configurable scope**      |
| ---------------------------- | ---------------------- | --------------------------- |
| Production HTTP interval     | 5 minutes              | Global / website / endpoint |
| Non-production HTTP interval | 15 minutes             | Global / website / endpoint |
| HTTP timeout                 | 15 seconds             | Global / endpoint           |
| Availability confirmation    | 2 consecutive failures | Monitor policy / endpoint   |
| Recovery confirmation        | 2 consecutive passes   | Monitor policy / endpoint   |
| Slow response confirmation   | 3 consecutive breaches | Monitor policy / endpoint   |
| Response warning / critical  | 1,500 / 3,000 ms       | Endpoint                    |
| Redirect hop limit           | 10                     | Global / endpoint           |
| SSL check interval           | 24 hours               | Global / endpoint           |
| SSL warning bands            | 30 / 15 / 7 days       | Global / client             |
| Unacknowledged reminder      | 60 minutes             | Escalation policy           |
| First escalation             | 30 minutes             | Escalation policy           |
| Crawler maximum pages        | 1,000                  | Crawl profile / run         |
| Crawler maximum depth        | 5                      | Crawl profile / run         |
| Crawler request rate         | 2 requests/second/host | Crawl profile               |
| HTML page-size warning       | 2 MB                   | Endpoint                    |
| Raw result retention         | 90 days                | Global                      |
| Aggregate/incident retention | 24 months              | Global                      |
| Company display timezone     | Asia/Beirut            | Global / user               |

# Appendix B. Glossary

| **Term**        | **Meaning**                                                                                  |
| --------------- | -------------------------------------------------------------------------------------------- |
| Eligible sample | A logical check result included in a particular calculation after exclusion rules.           |
| Issue key       | Stable normalized identifier used to match repeated findings to the same incident.           |
| Logical check   | One intended monitor execution, regardless of infrastructure retry attempts.                 |
| P50 / P95       | Response-time percentiles: half / 95% of eligible measurements are at or below the value.    |
| SSRF            | Server-Side Request Forgery: abuse of a server to request unauthorized network destinations. |
| TTFB            | Time to First Byte, measured from request start until the first response byte.               |
| Uptime          | Percentage of eligible availability samples classified as healthy during a report window.    |

# Approval

This document is the implementation baseline when the intern records acceptance. Any material change to scope, severity, thresholds, permissions, incident lifecycle, or retention must be documented and self-reviewed.

| **Owner** | **Decision** | **Date** |
|---|---|---|
| Intern | Accepted / Changes Required |  |

Optional mentor or peer feedback may be linked separately but is not required approval.
