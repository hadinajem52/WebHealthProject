# Detail Page UI Pattern

**Status:** Implemented across the registry and operational detail pages tracked in §8. The final legacy `registry-facts` page, `Views/Targets/Environment.cshtml`, migrated during the endpoint-first Phase 6 polish pass.
**Figma reference:** [Purity UI — Endpoint, node `1665:345`](https://www.figma.com/design/cjTsi6qaX3bH0l3a4vF7Jm/Purity-UI-Dashboard---Chakra-UI-Dashboard--Community-?node-id=1665-345)
**Extends:** [`../phase-0/UI_Direction.md`](../phase-0/UI_Direction.md). Nothing here supersedes semantic HTML, server-side authorization, anti-forgery, or accessibility requirements.

## 1. Why this document exists

The endpoint page was rebuilt to match the Figma. The result is the reference shape for every
**detail page** in the application — a page that shows one record's facts plus the actions you can
take on it. This document records what changed and why, so the rest of the site can be brought onto
the same pattern without rediscovering the decisions or repeating the mistakes.

A detail page is not a list page. Index pages keep `.data-table`; this pattern is for the
single-record view behind them.

## 2. Before and after

### Hierarchy

| | Before | After |
|---|---|---|
| Card title | A generic label (`Endpoint`) | The record's own identity — the URL, in mono type |
| Record state | A `Status` row buried among eight other facts | A status eyebrow directly under the title, with a state-coloured icon |
| Primary actions | Four equal flat buttons competing for attention | One icon tile, one primary split action, one primary link, one secondary menu |
| Destructive actions | A loose `registry-lifecycle` button strip at the bottom of the card, visually equal to everything else | Collected inside the **Actions** menu, red-tinted, behind one click |

The core change is that the page now has a **single focal point** (the record identity plus its
state) instead of a flat field of equally weighted controls, and destructive operations are no
longer one stray click away.

### Layout

Before, facts were a `registry-facts` grid: `repeat(auto-fit, minmax(12rem, 1fr))` of sunken grey
tiles, each an uppercase label stacked above a value. It reflowed into an unpredictable number of
columns, so no two facts ever lined up between cards, and long values (a certificate DN, a SHA-256
fingerprint) either blew out a tile or wrapped into a ragged block.

After, facts are a `detail-list`: a fixed two-column grid, `minmax(0, 1fr) minmax(0, 19rem)`, with a
`Subject` / `STATUS` column header and one row per fact. Every card on the page shares the same two
column positions, long values wrap inside a known width, and the eye can scan the status column
straight down.

### Tables

The `detail-list` is deliberately **not** a `<table>`. It is a `<ul>` of rows, because the content is
a set of name/value facts about one record, not tabular data with meaningful columns. That keeps the
markup honest for screen readers and lets a row carry a badge, a note, and a value together. The
column header is `aria-hidden` — it is a visual affordance, not structure.

Row content moved from placeholder to real: the Figma showed five rows per card as placeholders, and
those were replaced with the endpoint's actual facts (9 rows on the endpoint card, 9 on certificate,
6 on latest check). **No fact from the previous design was dropped.** Secondary detail that used to
be appended inline — `(website inherited)`, `Reason: …`, `(endpoint override)` — became a muted
`.detail-list__note` instead of running into the value text.

## 3. Page anatomy

```html
<section class="card" aria-labelledby="thing-heading">
  <div class="page-intro">
    <div>
      <h2 class="card__title" id="thing-heading">Record identity</h2>
      <p class="card__status" data-status="success">
        @await Html.PartialAsync("_Icon", StatusIcon(status))
        <span>Thing status &middot; Enabled</span>
      </p>
    </div>
    <div class="form-actions"><!-- see §5 --></div>
  </div>

  <div class="detail-list">
    <div class="detail-list__head" aria-hidden="true">
      <span>Subject</span><span>Status</span>
    </div>
    <ul class="detail-list__items">
      <li>
        <div class="detail-list__row">
          <span class="detail-list__subject">
            <span class="detail-list__icon">@await Html.PartialAsync("_Icon", "person")</span>
            <span>Owner</span>
          </span>
          <span class="detail-list__value">
            <span>Alice</span>
            <span class="detail-list__note">Website inherited</span>
          </span>
        </div>
      </li>
    </ul>
  </div>
</section>
```

Cards stack top to bottom, most important first. On the endpoint page: the record itself, then
certificate, then latest check.

## 4. Component reference

All classes live in `wwwroot/css/components.css`.

| Class | Purpose |
|---|---|
| `.card__status` | Status eyebrow under a card title. `data-status="success\|warning\|danger"` colours the icon. Pair with a `StatusIcon()` helper so the glyph changes too — never colour one glyph three ways (§7). |
| `.detail-list` | Wrapper. `margin-top: var(--space-6)`. |
| `.detail-list__head` | `Subject` / `STATUS` column header. Carries the one heavier rule on the list. |
| `.detail-list__items` | The `<ul>`. Unstyled list. |
| `.detail-list__row` | The two-column grid inside each `<li>`. |
| `.detail-list__subject` | Left cell: icon + label. |
| `.detail-list__icon` | Teal (`--color-teal-300`) 14px glyph well. |
| `.detail-list__value` | Right cell: badge, text, or both. Wraps. |
| `.detail-list__note` | Muted secondary detail inside a value. |
| `.button--icon` | Square icon-only button (the 45×39 tile in the Figma). |
| `.button__caret` | Chevron wrapper on a dropdown trigger. Do not set a size on it — it inherits `.button svg`'s 1rem so it matches the leading icon. |
| `.action-menu` | Dropdown container. See §5. |

### Value cell: badge or text?

Use a **badge** (`_StatusBadge`) when the value is a verdict with a fixed vocabulary — a state, a
validation category, an outcome, an authorization result. Use **plain text** for measurements,
timestamps, identifiers, and free-form values. Do not badge something just to add colour; a page
where everything is a badge conveys no priority. Map badge styles through `Shell/StatusBadges.cs`
(`ForOutcome`, `ForHealthStatus`, `ForExpirySeverity`, …) rather than writing a local `switch` — that
class exists because the list and detail pages had each grown their own drifting copy.

## 5. Action row

Order, left to right: **secondary icon tile → primary split action → primary link → secondary menu.**

```html
<div class="form-actions">
  <a class="button button--secondary button--icon" title="Check history" aria-label="Check history">…</a>

  <div class="action-menu" data-shell-menu data-open="false">
    <button type="button" class="button button--primary"
            data-shell-menu-toggle aria-controls="run-menu"
            aria-expanded="false" aria-haspopup="true">
      @await Html.PartialAsync("_Icon", "play")
      <span>Run</span>
      <span class="button__caret">@await Html.PartialAsync("_Icon", "chevron-down")</span>
    </button>
    <div class="action-menu__panel" id="run-menu" data-shell-menu-panel>
      <form method="post" asp-action="…">
        <button class="action-menu__item" type="submit">…</button>
      </form>
      <hr class="action-menu__separator" />
      <form method="post" asp-action="…">
        <button class="action-menu__item action-menu__item--danger" type="submit">…</button>
      </form>
    </div>
  </div>
</div>
```

Rules:

- **An icon-only button needs both `title` and `aria-label`.**
- **Menu items are real forms**, one per action, each with its hidden `id` and `version` inputs. The
  anti-forgery token comes from the Tag Helper. A menu is a presentation choice; the server still
  enforces role and state on every request.
- **Destructive items** get `.action-menu__item--danger`, and an irreversible one gets
  `data-shell-confirm="…"` on the form.
- **`data-shell-menu` / `-toggle` / `-panel` is the whole JS contract.** `wwwroot/js/shell.js`
  binds every `[data-shell-menu]` on the page through the same `setUpPopupMenu` used by the header
  and dashboard filter menus, so Escape, click-away, and focus-out behaviour cannot drift between
  them. Add no new dropdown script.
- **Without JavaScript the panel is visible and every item still submits.** The `display: none` that
  hides a closed panel is scoped to `.js`, which `shell.js` sets on `<html>` before first paint.

## 6. Icons

`Views/Shared/_Icon.cshtml` is one `switch` over 47 keys resolving to 45 glyphs — two glyphs
are shared by a pair of keys each (`incidents`/`warning`, `seo`/`search`), grouped rather than
duplicated. 44 of the 45 come from the
[coolicons free iconset](https://www.figma.com/design/6kxYmopJJaQLr7VWzgN7u4/coolicons-%7C-Free-Iconset--Community---Copy-?node-id=17102-2265),
24×24 on a 2px stroke, each annotated with its source name (`@* coolicons Interface/Settings *@`).
`brand` is the 45th and the exception — it is the product mark, not an iconset glyph.

- **Every glyph is `stroke="currentColor" fill="none"`.** Colour comes from the calling component:
  teal in a list row, white on a primary button, `--status-danger-text` on a danger menu item. Never
  hardcode a colour inside `_Icon.cshtml`.
- **Size comes from the caller too**, via a rule on the parent (`.button svg`, `.detail-list__icon svg`).
- Adding an icon means pulling it from that Figma file, not drawing one by hand. The geometry is
  `viewBox="0 0 24 24"` with a `<g transform="translate(x y)">` where `x = insetLeft% × 24 − 1` and
  `y = insetTop% × 24 − 1` — the `−1` removes the 1px stroke pad Figma adds to each export.

## 7. Rules learned the hard way

Each of these came from a defect in this rebuild. They read as fussy; they are not.

1. **Row separators use `--border-subtle`, never `--divider-line`.** `--divider-line` is a gradient
   that is transparent at 0%, opaque at 49.52%, and faded at 99%. It is the *card* divider
   (`.card__divider`). Applied to list rows it makes every separator peak in the middle, which reads
   as a random mix of bold and faint lines. The Figma draws all row rules as one flat `#E2E8F0`
   stroke.
2. **Give list rows whole-pixel line boxes.** `--line-height-tight` is the unitless `1.4`, so a 14px
   row is 19.6px and each separator lands on a different fraction of a device pixel — at `.0` a 1px
   line is crisp and dark, at `.5` it smears across two pixels and looks lighter. Same colour,
   different apparent weight. `.detail-list__subject` and `.detail-list__value` set
   `line-height: 20px`. Use a **length, not a ratio**: it inherits as 20px into the smaller
   `.detail-list__note`, where `1.4` would recompute to 16.8px and put the fraction straight back.
3. **Only one heavier rule per list**, the one under the column header
   (`.detail-list__head` `border-bottom: 1px solid var(--border-strong)`). Row separators are
   `> li + li::before`, so the first row does not draw a second line under that border.
4. **The status eyebrow changes glyph, not just colour.** A red checkmark is not a failure icon.
   Map `success → success`, `danger → error` (X in circle), everything else → `warning`. Colour alone
   is not an accessible signal.
5. **Do not force a size on `.button__caret`.** Let it inherit `.button svg`'s 1rem so the chevron
   matches the leading icon, and make the wrapper `inline-flex` so the glyph centres on the label
   instead of sitting on its text baseline.
6. **`.card__title` is `font-weight: 400`**, per the Figma. This is a global rule — it applies to
   every card title in the app.

## 8. Applying this to another page

Pages still on the old pattern, in rough order of value:

Already migrated: `Views/Targets/Endpoint.cshtml`, `Views/Checks/Check.cshtml`,
`Views/Incidents/Details.cshtml`, `Views/Maintenance/Details.cshtml`, and the list-page variant
(header and eyebrow only; the table stays a `.data-table`) on `Views/Checks/History.cshtml`,
`Views/Maintenance/Index.cshtml` and `Views/Maintenance/Archived.cshtml`.

Also migrated: `Views/Registry/Website.cshtml`, `Views/Registry/Client.cshtml`, and
`Views/Targets/Environment.cshtml`. The Crawl and PageSpeed views no longer use either legacy
class; their current run and list layouts remain appropriate to their records. No page under
`Views` now uses `registry-facts` or `registry-lifecycle`.

Checklist per page:

- [ ] Card title becomes the record's identity; add a `.card__status` eyebrow with a `StatusIcon()` helper.
- [ ] `registry-facts` becomes a `detail-list`; every existing fact survives as a row.
- [ ] Pick an icon per row from the existing 47 keys before adding a new one.
- [ ] Inline parenthetical detail becomes `.detail-list__note`.
- [ ] `registry-lifecycle` buttons move into an **Actions** menu; destructive items get
      `--danger` and, if irreversible, `data-shell-confirm`.
- [ ] Multiple run/execute buttons collapse into one split **Run ▾** menu.
- [ ] Badge styles come from `StatusBadges`, not a local `switch`.
- [ ] Authorization conditions (`CanManage`, `CanTest`, `CanPurge`) are preserved exactly — a menu
      hides nothing the server would otherwise allow.
- [ ] The page works with JavaScript disabled.

## 9. Files

| File | Contains |
|---|---|
| `Views/Targets/Endpoint.cshtml` | The reference implementation |
| `wwwroot/css/components.css` | `.detail-list`, `.card__status`, `.action-menu`, `.button--icon`, `.button__caret` |
| `wwwroot/css/tokens.css` | `--border-subtle`, `--border-strong`, `--divider-line`, `--color-teal-300`, `--color-green-300` |
| `wwwroot/js/shell.js` | `setUpPopupMenu` and the `[data-shell-menu]` binding |
| `Views/Shared/_Icon.cshtml` | 47 icon keys over 45 glyphs (44 coolicons + `brand`) |
| `Views/Shared/_StatusBadge.cshtml`, `Shell/StatusBadges.cs` | Badge markup and vocabulary mapping |
