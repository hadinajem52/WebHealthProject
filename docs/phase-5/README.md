# Phase 5 — SSL, Dashboard, Trends, Reports, and CSV

This folder records Phase 5 implementation evidence. The phase gate and its checkboxes live in [`../General/Phased_Implementation_Plan.md`](../General/Phased_Implementation_Plan.md); nothing here marks a gate item complete on its own.

| Increment | Status | Evidence |
|---|---|---|
| 5.1 Certificate capture in the safe transport | Complete | [Evidence](Certificate_Capture_and_Safe_Tls_Inspection.md) |
| 5.2 SSL monitor type, persistence, and scheduling | Planned | — |
| 5.3 SSL severity, deduplication, and renewal | Planned | — |
| 5.4 Performance rules (BR-P01–BR-P05) | Planned | — |
| 5.5 Shared reporting query core and CSV export | Planned | — |
| 5.6 Dashboard, trends, and reports UI | Planned | — |
| 5.7 Query plans, performance baseline, and completion gate | Planned | — |

## Decisions recorded in this phase

| Decision | Where |
|---|---|
| SSL is a second monitor type on the existing scheduling/lease/incident pipeline, not a parallel one | 5.2 |
| The certificate probe records and rejects: a validation callback that always returns `false`, so invalid-certificate evidence exists without ever accepting one | [5.1](Certificate_Capture_and_Safe_Tls_Inspection.md) |
| Validation-category precedence, and an inclusive RFC 5280 validity window | [5.1](Certificate_Capture_and_Safe_Tls_Inspection.md) |
| Exact severity semantics at the 30/15/7-day boundaries | 5.3 |
| `percentile_cont` for response-time P50/P95 | 5.5 |
| CSV encoding, quoting, and formula-injection handling | 5.5 |
| Daily aggregates deferred to Phase 7 unless query-plan evidence requires them earlier | 5.7 |
