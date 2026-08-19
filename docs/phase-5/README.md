# Phase 5 — SSL, Dashboard, Trends, Reports, and CSV

This folder records Phase 5 implementation evidence. The phase gate and its checkboxes live in [`../General/Phased_Implementation_Plan.md`](../General/Phased_Implementation_Plan.md); nothing here marks a gate item complete on its own.

| Increment | Status | Evidence |
|---|---|---|
| 5.1 Certificate capture in the safe transport | Complete | [Evidence](Certificate_Capture_and_Safe_Tls_Inspection.md) |
| 5.2 SSL monitor type, persistence, and scheduling | Complete | [Evidence](Ssl_Monitor_Type_and_Scheduling.md) |
| 5.3 SSL severity, deduplication, and renewal | Complete | [Evidence](Ssl_Severity_Deduplication_and_Renewal.md) |
| 5.4 Performance rules (BR-P01–BR-P05) | Complete | [Evidence](Performance_Rules.md) |
| 5.5 Shared reporting query core and CSV export | Complete | [Evidence](Shared_Reporting_Query_Core.md) |
| 5.6 Dashboard, trends, and reports UI | Complete | [Evidence](Dashboard_Trends_And_Reports_Ui.md) |
| 5.7 Query plans, performance baseline, and completion gate | In progress — NFR-02 not yet met | [Evidence](Query_Plans_And_Performance_Baseline.md) |

## Planned verification

| Checklist | Purpose |
|---|---|
| [Browser-driven UI test checklist](Phase_5_Ui_Test_Checklist.md) | The Playwright cases to write once Phase 5's surfaces are settled — claims that can only be falsified in a real browser, deliberately excluding rules the xUnit suites already prove. |

## Decisions recorded in this phase

| Decision | Where |
|---|---|
| The certificate probe records and rejects: a validation callback that always returns `false`, so invalid-certificate evidence exists without ever accepting one | [5.1](Certificate_Capture_and_Safe_Tls_Inspection.md) |
| Validation-category precedence, and an inclusive RFC 5280 validity window | [5.1](Certificate_Capture_and_Safe_Tls_Inspection.md) |
| SSL is a second monitor type on the existing scheduling/lease/incident pipeline, not a parallel one | [5.2](Ssl_Monitor_Type_and_Scheduling.md) |
| Certificate results never count toward uptime | [5.2](Ssl_Monitor_Type_and_Scheduling.md) |
| The migration backfills certificate monitors, with SQL/application fingerprint parity pinned by a test | [5.2](Ssl_Monitor_Type_and_Scheduling.md) |
| Exact severity semantics at the 30/15/7-day boundaries, inclusive on the unhealthy side | [5.3](Ssl_Severity_Deduplication_and_Renewal.md) |
| One expiry rule covers approaching and reached expiry, keyed by fingerprint | [5.3](Ssl_Severity_Deduplication_and_Renewal.md) |
| `High` is a finding and incident severity, not an endpoint health state | [5.3](Ssl_Severity_Deduplication_and_Renewal.md) |
| Threshold boundaries are inclusive on the unhealthy side, matching the certificate bands | [5.4](Performance_Rules.md) |
| Slow response confirms on its own three-breach count; recovery is counted per issue | [5.4](Performance_Rules.md) |
| Page size prefers the advertised transferred length, and the measurement is labelled | [5.4](Performance_Rules.md) |
| Existing 1,000 ms monitors are not backfilled; the new override surface is the remedy | [5.4](Performance_Rules.md) |
| One filter object and one query layer, so AC-11 holds by construction | [5.5](Shared_Reporting_Query_Core.md) |
| Uptime is healthy over eligible (BR-U01); the reachable figure is reported beside it, and every sample predicate is written positively | [5.5](Shared_Reporting_Query_Core.md) |
| `percentile_cont` for response-time P50/P95 | [5.5](Shared_Reporting_Query_Core.md) |
| CSV encoding, quoting, and formula-injection handling on user text only | [5.5](Shared_Reporting_Query_Core.md) |
| Chart.js vendored locally; the chart always has a table equivalent | [5.6](Dashboard_Trends_And_Reports_Ui.md) |
| Non-colour status cues are a shape on `.badge` itself, not on a partial | [5.6](Dashboard_Trends_And_Reports_Ui.md) |
| Plans are captured with `auto_explain`, so the evidence is about the query the application sent | [5.7](Query_Plans_And_Performance_Baseline.md) |
| The monitor identity is carried on the sample, guarded by a composite foreign key, so one index serves both halves of every reporting predicate | [5.7](Query_Plans_And_Performance_Baseline.md) |
| Comparability asks for the two facts BR-P05 turns on rather than every distinct pair | [5.7](Query_Plans_And_Performance_Baseline.md) |
| Daily aggregates: decision open until the dashboard meets NFR-02 on measured evidence | [5.7](Query_Plans_And_Performance_Baseline.md) |
