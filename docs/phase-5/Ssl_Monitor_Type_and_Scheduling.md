# SSL Monitor Type, Persistence, and Scheduling

**Work item:** Phase 5 / increment 5.2  
**Rules:** BR-C01, BR-C02, BR-C03, BR-C07, BR-U03, BR-U05  
**Acceptance criteria contribution:** AC-06 observation and scheduling. The 30/15/7-day severity boundaries, fingerprint-based expiry deduplication (BR-C05) and renewal resolution (BR-C06) are increment 5.3.

## Delivered boundary

Certificate checking is a **second monitor type on the existing pipeline**, not a parallel one. `SslCertificate` monitors are dispatched by the same scheduler, take the same per-endpoint lease, create the same logical checks, and finalize through the same transaction as availability checks. A certificate check therefore inherits Phase 3's idempotency, lease fencing, restart reconciliation and retry accounting without any of it being reimplemented.

The only genuinely different step is the observation itself. `LogicalCheckExecutionService` branches on the snapshotted monitor type: HTTP monitors call the safe transport, SSL monitors call the `ISslCertificateProbe` delivered in increment 5.1. Everything on either side of that call is shared.

Durable work kind now follows the monitor type through `MonitorWorkKinds`, so a certificate check can never be queued as HTTP work (or vice versa) — the executor and the finalizer both resolve the expected kind from the same function.

## Monitor lifecycle (BR-C01)

A certificate monitor exists exactly while an endpoint is HTTPS:

- creating an HTTPS endpoint creates both an availability monitor and a certificate monitor;
- creating an HTTP endpoint creates only the availability monitor;
- editing an endpoint from HTTP to HTTPS adds a certificate monitor, and from HTTPS to HTTP retires the existing one by soft deletion;
- pausing an endpoint's schedule pauses **every** monitor on it. A "paused" endpoint that still raised certificate incidents would not be paused at all.

The endpoint page shows **Not Applicable** for an HTTP-only endpoint rather than Unknown, and "no certificate checked yet" for an HTTPS endpoint awaiting its first daily run.

Defaults: a 24-hour interval (BR-C07), a 15-second timeout, and single-observation confirmation. Unlike a flapping HTTP response, an expired or untrusted certificate does not resolve itself between daily checks, and requiring a second day to confirm would waste a day of the expiry window.

Adding a second monitor per endpoint invalidated several "the endpoint's monitor" assumptions that were correct while every endpoint had exactly one. Registry reads, the audit snapshot, the schedule toggle and the endpoint list projection now each select the availability monitor explicitly; left alone they would have thrown as soon as the first HTTPS endpoint gained a certificate monitor.

## Persistence

Migration `SslCertificateMonitoring` adds:

- `certificate_observation`, keyed by logical check, holding subject, issuer, serial, SHA-256 fingerprint, validity window, days remaining, validation category, hostname/trust flags, bounded SANs and the observation instant. **No private key material and no encoded certificate bytes are stored** (BR-C02);
- indexes on `(endpoint_monitor_id, observed_at DESC)` for "latest certificate for this endpoint" and on `sha256_fingerprint` for the fingerprint-keyed deduplication increment 5.3 will need;
- check constraints on the validity window, the fingerprint format and the category values;
- the five SSL failure categories added to the shared `check_result` category constraint;
- the seeded SSL policy profile;
- a backfill that gives every existing HTTPS endpoint the certificate monitor it would have been created with.

Certificate results are recorded in the shared `check_result` table but **never count for uptime**. Uptime is an availability measure (BR-U03, BR-U05); a certificate check says nothing about whether the site was reachable, so counting it would corrupt the availability figures increment 5.5 reports.

### The backfill fingerprint

The backfill has to write the same canonical v2 policy fingerprint the application computes, because dispatch rejects any check whose stored fingerprint does not recompute. A wrong value would not fail loudly — it would silently disable every backfilled monitor. The migration therefore reproduces the canonical string in SQL (each field written as its UTF-8 byte length, a colon, the value and a pipe; null written as `-1:`) and hashes it with `sha256()`.

That is exactly the kind of duplicated encoding that rots, so it is pinned by a test: the database gate asserts that the value the application stored, the value the migration's SQL computes for the same endpoint, and the value `RegistryDefaults.CreateSslFingerprint` produces today are all identical.

## Urgent re-check (BR-C07)

A TLS-category availability failure requests an out-of-band certificate check instead of waiting for the next daily slot. Three properties matter:

- **It is requested after the availability result commits**, never inside the finalization transaction, so an urgent check can never be queued for a result that was rolled back.
- **Only availability checks trigger it.** A failing certificate check never requests another one, or a permanently broken host would re-queue itself forever.
- **One per endpoint per cooldown window** (default one hour, configurable, validated between 5 minutes and 1 day). The cooldown counts urgent checks only, so the daily scheduled check is never suppressed by it.

The urgent check is created as a `Urgent`-source logical check with its own durable work, so if the enqueue never lands, reconciliation recovers it exactly like any other committed work.

## Verification evidence

Unit tests (`SslResultNormalizerTests`, 16 cases) cover the observation-to-result mapping: a valid certificate is healthy with no findings; each of the four invalid categories is critical with its own category and issue key (BR-C03); a handshake failure is critical without certificate evidence; transport-level failures reuse the shared categories; and a cancelled probe raises no finding, so it cannot open an incident. `CertificateExpiryTests` pins days-remaining truncation and the negative count for already-expired certificates.

The PostgreSQL database gate additionally verifies, against a real PostgreSQL 18 cluster:

- an HTTPS endpoint gains a certificate monitor with the daily interval and SSL policy profile, and an HTTP endpoint gains none;
- the backfill fingerprint parity described above;
- a TLS failure queues exactly one urgent certificate check with `SslCheck` durable work, a second TLS failure inside the cooldown queues none, and a non-TLS failure queues none.

## Remaining work

Increment 5.3 adds days-remaining severity bands at the exact 30/15/7-day boundaries, fingerprint-keyed expiry deduplication (BR-C05), and renewal detection that resolves the previous certificate's incident after confirmation (BR-C06).
