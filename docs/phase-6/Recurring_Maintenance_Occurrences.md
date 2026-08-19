# Recurring maintenance occurrences (timezone-aware)

**Work item:** Phase 6.1
**Rules:** BR-M05; BR-M01–M04 and AC-09 regression-tested unchanged
**Acceptance contribution:** BR-M05; AC-09 regression

## 1. The decision that comes first — daylight saving

A recurring window is declared as a *local wall-clock* time of day in an IANA timezone. Twice a year
that wall-clock time is not a single instant:

- **Spring forward (gap).** The nominal local time does not exist. A 02:30 window in
  `Europe/Berlin` has no 02:30 on the transition day.
- **Autumn back (ambiguity).** The nominal local time exists twice, once at the pre-transition
  (daylight) offset and once at the post-transition (standard) offset.

BR-M05 requires that these transitions "do not create ambiguous occurrences". The rule is therefore
written down before any expansion code, and both transition days are direct unit tests.

### 1.1 Rule — gap: shift forward by the length of the gap

A nominal local start that falls in a gap is resolved with the **pre-transition offset**. This yields
the first instant at or after the transition, whose local time is the nominal time plus the gap
length: a 02:30 window in a 02:00→03:00 gap runs at 03:30 local.

Rejected alternative — *skip the day*. Skipping silently removes maintenance suppression on exactly
one day a year, on the one day an operator is least likely to be checking. The invariant that
matters operationally is **one occurrence per scheduled local date**, and shifting preserves it.

### 1.2 Rule — ambiguity: the first (earlier) instant, exactly once

A nominal local start that is ambiguous is resolved with the **largest UTC offset among the
ambiguous offsets**, which is the *earlier* of the two instants (a larger offset means an earlier
instant for the same wall time). Exactly one occurrence is produced.

Rejected alternative — *run both*. Two occurrences for one scheduled date is precisely the
"ambiguous occurrence" BR-M05 forbids, and it double-books a window whose key is the occurrence
start.

### 1.3 Duration is absolute, never re-derived from wall time

An occurrence end is `start + duration`, where duration is the absolute length of the declared
window. It is **not** recomputed as a local end-of-day time. A window that spans a transition is
therefore the same number of minutes as on every other day; it does not silently grow or shrink by
an hour. An operator who needs the repeated hour covered lengthens the window.

### 1.4 The stored anchor is canonical

An operator can declare an anchor on the *second* pass of an ambiguous local time. Expansion
resolves that wall time to the earlier instant (§1.2), so storing the declared instant unchanged
would make the anchor unreachable: the anchor day would resolve an hour earlier than
`schedule_starts_at` and be filtered out, and the window would silently lose its first occurrence.

`schedule_starts_at` is therefore stored **canonicalised** for recurring windows — the declared
local wall-clock time, resolved by the same rule every later occurrence uses. A one-off window is
never moved: it has no recurrence to agree with, and its instant is exactly what was declared.

### 1.5 Comparison direction is unchanged

`MaintenanceInterval.Contains` stays start-inclusive / end-exclusive on UTC instants. All recurrence
arithmetic happens in local wall time; only fully resolved instants are ever compared. There is no
local-time comparison anywhere in the suppression path.

## 2. Schedule specification versus materialised occurrences

The window row carries the **schedule specification**; `maintenance_occurrence` rows carry the
**materialised instants**. Suppression reads only occurrences — it never recomputes a recurrence
during a check, which is what keeps `MaintenanceEvaluator` a single indexed query.

Window columns added by `RecurringMaintenanceOccurrences`:

| Column | Meaning |
|---|---|
| `schedule_starts_at` | UTC instant of the first (anchor) occurrence, stored canonicalised (§1.5). Its local time in the window timezone is the recurring wall-clock time. |
| `schedule_duration_seconds` | Absolute occurrence length, > 0. Seconds, not minutes: the declared interval must round-trip exactly, and a sub-minute window is valid. Creation rejects a duration that is not a whole number of seconds. |
| `recurrence_pattern` | `None`, `Daily`, or `Weekly`. |
| `recurrence_days_of_week` | Weekly day bitmask, Sunday = 1 … Saturday = 64. `0` unless `Weekly`. |
| `recurrence_until` | Exclusive UTC bound; `NULL` means open-ended. |
| `expanded_through` | Horizon watermark: occurrences are materialised for every start strictly before this instant. |

Constraints: `recurrence_pattern` is check-constrained to the three values; a `None` window must have
mask `0` and no `recurrence_until`; a `Weekly` window must have a mask in `1..127`; a `Daily` window
must have mask `0`; `recurrence_until`, when set, must be after `schedule_starts_at`.

For a `Weekly` window the anchor's own local day-of-week must be in the mask. This is validated on
create, and keeps the invariant that the first materialised occurrence is exactly the declared start.

## 3. Idempotent expansion

The occurrence uniqueness key is now **(window, occurrence start)** —
`ux_maintenance_occurrence_window_start`, replacing the old `(window, start, end)` index. The end is
a function of the start and the schedule duration, so including it in the key allowed a second row
for the same start under a changed duration; the narrower key is the one the expander needs.

`MaintenanceOccurrenceExpander` therefore:

- reads the schedule, expands `[from, horizon)` purely in `MaintenanceRecurrence`, where
  `from = expanded_through ?? schedule_starts_at`;
- inserts only starts that are not already present, and **never updates or deletes** — occurrence
  rows are immutable at the database level (`trg_maintenance_occurrence_immutable`);
- treats a unique violation from a concurrent expander as a no-op;
- advances `expanded_through` to the horizon.

Consequences, both of which are asserted by tests:

- Re-running the expander over the same horizon writes nothing and cannot double-book a window.
- Extending the horizon appends future occurrences and rewrites no history, so occurrences already
  referenced by `check_result.maintenance_occurrence_id` stay valid forever.

Expansion runs inline on create (so a window is immediately effective) and from the
`maintenance-occurrence-expansion` recurring Hangfire job, which is hourly and bounded by
`Maintenance:Scheduling:HorizonDays` (default 90) and `BatchSize`. The job is **enabled in
`appsettings.json`**: without it only the first horizon is ever materialised, and an open-ended
recurrence would stop suppressing once that horizon passed.

Two failure modes are handled deliberately rather than by default:

- **Unresolvable timezone.** If the window's zone is not available on the host, expansion fails
  closed — it logs and leaves `expanded_through` untouched, so the next tick retries. Advancing the
  watermark on a failed expansion would skip that horizon permanently and leave the period
  unsuppressed.
- **Concurrent expanders.** The watermark advance is conditional
  (`WHERE expanded_through IS NULL OR expanded_through < horizon`), so a slower worker cannot move
  it backwards onto the shorter horizon it computed earlier.

Cancelling a window soft-deletes it. Occurrences are left in place — historical results still point
at them — and `MaintenanceEvaluator` already excludes occurrences whose window is deleted, so future
occurrences of a cancelled recurrence suppress nothing.

## 3.1 Migration preflight

The migration recovers each existing window's schedule specification from the occurrence it owns.
Two legacy shapes would make that recovery wrong rather than merely incomplete, so both are checked
before any structural change and stop the migration with an actionable message:

- **Duplicate `(window, starts_at)` pairs**, which the old three-column key permitted. These cannot
  be de-duplicated automatically — `check_result.maintenance_occurrence_id` may reference either
  row, so which one survives is a person's decision.
- **A window with no occurrence**, which has no schedule to recover. Inventing a default would
  produce a window that silently suppresses a period nobody chose.

## 4. Editing a recurrence

Unchanged from Phase 4: editing cancels the window and creates a replacement, so the recurrence
specification is immutable for the lifetime of a window id and no already-materialised occurrence is
ever contradicted by a later edit.

## 5. Verification

Unit — `MaintenanceRecurrenceTests`:

- gap day shifts forward by the gap and produces exactly one occurrence;
- ambiguous day resolves to the earlier instant and produces exactly one occurrence;
- occurrence duration is constant across both transition days;
- daily and weekly expansion, weekly mask filtering, `recurrence_until` exclusivity, horizon bound;
- resuming from a watermark yields exactly the new tail, and re-expanding the same range yields the
  same instants (the pure half of idempotency);
- `None` expands to exactly the declared occurrence.

Integration — `DatabaseFoundationAssertions`:

- the `(window, start)` uniqueness key rejects a duplicate occurrence start;
- a recurring window declared on an ambiguous local instant still materialises its declared start;
- expanding twice over one horizon leaves the occurrence count unchanged;
- extending the horizon adds only later occurrences and leaves earlier rows byte-identical;
- **AC-09 regression:** a scheduled failure inside a *recurring* occurrence is retained, marked
  maintenance, linked to its occurrence, excluded from uptime, and opens no incident — the same
  assertions Phase 4 makes for a one-off window.
