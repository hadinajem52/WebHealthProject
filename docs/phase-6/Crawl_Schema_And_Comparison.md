# Crawl schema, results, and comparison (BR-L06, BR-L07, BR-L10)

**Work item:** Phase 6.7
**Acceptance contribution:** completes AC-08 with 6.5 and 6.6; the precondition for the 6.8 views
**Depends on:** 6.6 (`ICrawlResultSink`, the classifications and stop reasons it produces)

## 1. The decision that comes first — the filter columns live on the result row

`crawl_link_result` is the highest-volume table in this phase by a wide margin. One run of a
thousand pages produces one row per distinct source-target pair, which on an ordinary site is tens
of thousands of rows, and every run adds another set.

Phase 5 lost time to exactly one shape: the filter predicate and the window predicate living on
different tables, where no single index could serve the query. The reporting layer will ask

> the broken links in **this run**, and how they compare to the **previous** one

so `run_id`, `classification`, `is_internal` and `recorded_at` are **columns on the result row**, not
facts reached by joining to `crawl_run`. The composite index

    ix_crawl_link_result_run_classification  (run_id, classification)

ships in the **first** migration, not after a slow report is noticed. `run_id` leads because it is
always an equality predicate and it is the most selective column in the table; `classification`
follows because it is the only other predicate the broken-link views apply.

A second index, `(run_id, is_internal, classification)`, is deliberately **not** created. It would
duplicate the first for a boolean with two values, and PostgreSQL can filter `is_internal` from the
heap rows the first index already located.

**Plans are captured against seeded data before the 6.8 views are written**, not afterwards — the
same discipline 5.7 applied to the dashboard. `CrawlSchemaAssertions` seeds twelve runs of four
hundred results each, runs `ANALYZE`, and asserts the plan for the broken-link filter names the
index and contains no sequential scan. The volume matters: a plan assertion against ten rows proves
nothing, because PostgreSQL would scan that table whatever indexes existed.

## 2. Source-target uniqueness within a run (BR-L07)

The natural key of a result is `(run_id, source_url, target_url)`. It is **not** the stored key,
because URLs are up to 2048 characters each and PostgreSQL's btree entries cannot exceed roughly
2704 bytes — a unique index over two full URLs is not merely large, it can fail to insert.

So uniqueness is enforced over **SHA-256 hashes** of the canonical URLs, the same device
`endpoint.normalized_url_hash` already uses:

    ux_crawl_link_result_pair  UNIQUE (run_id, source_url_hash, target_url_hash)

The full URLs are stored alongside, because a report that could only show a hash would be useless.
The hash is identity; the text is evidence.

**A seed has no source page,** so `source_url` is null there. A null in a unique index is distinct
from every other null by default, which would let the same seed be inserted repeatedly. The index is
therefore declared `NULLS NOT DISTINCT`, so one run holds exactly one row per seed. Storing a
sentinel hash instead would have worked too, and was rejected: a magic 32-byte constant that must be
kept in step between the writer, the constraint and every reader is a rule nobody can see.

## 3. The run row

`crawl_run` records what 6.6's `CrawlRunOutcome` already decides — status, stop reason, counts, and
whether the robots override was granted or why it was refused.

Two constraints are worth naming because they encode BR-L10 and BR-L05 in the database rather than
in a convention:

- `ck_crawl_run_status_stop_reason`: a `Completed` run may only carry `FrontierExhausted`,
  `PageLimit` or `DurationLimit`; a `Cancelled` run may only carry `Cancelled`. A cancelled run can
  never be stored as complete, whatever a future caller does.
- `ck_crawl_run_override`: a granted override carries no refusal reason and a refused one carries a
  reason. An override that left no trace is exactly the silent flag this project refuses to have.

`finished_at` is null while a run is in flight and set once it stops, so an interrupted process
leaves a visibly unfinished run rather than one that looks complete.

## 4. Comparison between runs (new, continuing, resolved)

Two runs are comparable when they crawled the **same endpoint** and both **completed**. The
comparison is between the latest such run and the one before it. A first crawl has no previous run,
so `PreviousRunId` is null and every broken link is reported as new — which a reader can tell apart
from a run that genuinely introduced them precisely because the null is carried rather than hidden.

| Bucket | Definition |
|---|---|
| **New** | Broken in the current run, not broken in the previous one |
| **Continuing** | Broken in both |
| **Resolved** | Broken in the previous run, not broken in the current one |

"Not broken in the current run" covers two different things, deliberately folded together: the pair
was checked and is now healthy, and the pair no longer exists because the source page stopped
linking to it. Both are resolutions of that broken link, and distinguishing them would report a
removed link as still outstanding.

**A cancelled run is never used as the current side of a comparison.** It covered only part of the
site, so every link it did not reach would appear as resolved — a partial crawl would manufacture
good news. It can still be read on its own, labelled with its stop reason.

Comparison is a **query**, not a stored table. The buckets are derivable from two runs' rows at any
time, and materialising them would add a second thing to keep correct for no gain a composite index
does not already provide.

## 5. Retention rule (defined here, enforced in Phase 7)

Phase 7 owns retention. This increment defines the rule so that phase has something to enforce and
so the table is not designed as though rows live forever:

- **Crawl runs and their link results are kept for 90 days.**
- **The most recent terminal run per endpoint is kept regardless of age**, because deleting it would
  destroy the baseline every comparison in section 4 is measured against — a retention job that
  silently turned every link into a "new" finding would be worse than no retention at all.
- Deletion is by run: `crawl_link_result` cascades from `crawl_run`, so a run and its results leave
  together and a result can never outlive the run that explains it.

This is written down rather than implemented here for the same reason Phase 5 declined to build
rollups it could not size: enforcing retention before its owning phase would mean a deletion job
with no policy surface, no audit, and no way to switch it off.

## 6. What this increment does not do

No views and no authorization surface — those are 6.8. The reader this increment adds returns data;
the question of which roles may see it, and with which server-side filters, is answered where the
views are built.

## 7. Evidence

All of these run inside the database foundation gate, as the crawl stage that follows the SSL one.

| Rule | Where it lives | Assertion |
|---|---|---|
| BR-L07 source-target uniqueness | `ux_crawl_link_result_pair` | `VerifySourceTargetUniquenessAsync` |
| Seeds occur once despite a null source | `NULLS NOT DISTINCT` | `VerifySourceTargetUniquenessAsync` |
| BR-L10 cancelled is never complete | `ck_crawl_run_status_stop_reason` | `VerifyRunStatusContractAsync` |
| BR-L02 override recorded either way | `ck_crawl_run_override` | `VerifyOverrideContractAsync` |
| A result never outlives its run | `ON DELETE CASCADE` | `VerifyResultsCascadeWithTheirRunAsync` |
| Index actually serves the filter | `(run_id, classification)` | `VerifyReportingIndexServesTheFilterAsync` |
| New/continuing/resolved | `CrawlReportReader` | `VerifyComparisonAsync` |
| Results survive per link, not per run | `CrawlResultSink` | `VerifyComparisonAsync` |
