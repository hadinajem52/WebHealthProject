# Phase 1 Application Shell

## Scope

This work item delivers only the reusable application shell required by the Phase 1 gate:

- shared layout, sidebar navigation, header, breadcrumbs, flash messages and validation summary;
- skip link, semantic landmarks, keyboard focus behavior, responsive navigation and reduced-motion support;
- accessible error and empty-state components;
- a placeholder dashboard page that confirms the shell renders.

Dashboard widgets, registry pages, authentication screens and role-dependent navigation are **not** part of this work item. They belong to Phases 2–5.

## Design source

The visual baseline is the Purity UI Dashboard Figma file adopted in [`../phase-0/UI_Direction.md`](../phase-0/UI_Direction.md).

| Reference | Node | Used for |
|---|---|---|
| Dashboard Screen | `2:31` | Page frame, content background, card grid |
| Sidebar | `2:164` | Brand row, nav item geometry, section label, support card |
| Sidebar separator | `47:276` | Divider gradient and position |
| Breadcrumb (header) | `2:263`, `2:268` | Breadcrumb trail and page title |
| Header menu | `4:287` | Search field, account entry, utility icons |
| Today's Money (card) | `4:1` | Card radius, padding, shadow, icon tile |
| Feature card | `4:289` | Eyebrow, heading, supporting text, action link, side panel |
| Profile Information | `68:448` | Heading, description, divider, label/value rows |

Layout intent and tokens were taken from the file; markup, CSS, icons and interaction behavior are application-owned. The Purity navigation taxonomy (Tables, Billing, RTL, Profile, Sign In, Sign Up) was replaced by the information architecture in `UI_Direction.md` section 4, and the Creative Tim brand mark was replaced by an application-owned glyph in the same 22px box and Gray-700 color.

The sidebar support-card artwork (`wwwroot/images/sidebar-support.png`, 218x170) is the one exported Figma asset in the repository. The project owner supplied it directly on 2026-08-13 for this purpose.

### Typeface

The design was reset to Figtree, so the shell follows. Figtree is published under the SIL Open Font License 1.1, which removes the licence risk carried by the Helvetica Neue files this replaced.

Four self-hosted `woff2` subsets are declared in `tokens.css` with `font-display: swap`: upright and italic, each in the `latin` and `latin-ext` ranges. All four are variable fonts covering weights 300-900, so no weight is ever synthesised, and the unicode ranges match the Google Fonts subsetting, so a browser downloads the `latin-ext` files only when a page actually contains those characters. The four files total 61 KB. `--font-sans` keeps `system-ui, -apple-system, "Segoe UI", Arial, sans-serif` behind Figtree.

The files are self-hosted rather than requested from `fonts.googleapis.com`: the application makes no third-party request, works offline, and needs no external host in the Content Security Policy defined by later hardening work.

They must live under `wwwroot`. Linking them from elsewhere with `<Content Link="wwwroot\fonts\..." />` copies them to the build output only, so `dotnet run` answers every font request with an empty 200: the endpoint resolves from the manifest, but the file is absent from the project's physical `wwwroot`.

### Card anatomy

Figma nodes 4:289 and 68:448 define the card content structure:

| Part | Class | Value |
|---|---|---|
| Card | `.card` | White, 15px radius, `0 3.5px 5.5px rgba(0,0,0,.02)`, 21px padding |
| Eyebrow | `.card__eyebrow` | 12px bold, 3px above the heading |
| Heading | `.card__title` | 18px bold, line-height 1.4 |
| Supporting text | `.card__subtitle` | 14px regular, line-height 1.4 |
| Divider | `.card__divider` | The `--divider-line` gradient shared with the sidebar separator |
| Label/value rows | `.data-list` + `.data-list__row` | 12px bold label, 12px regular value, on one line per row, 36px pitch |
| Text action | `.card__action` | 12px bold with a 12px trailing arrow |
| Feature card | `.card--feature` | Text column beside a decorative panel, stacking below 62em |

Each row is its own flex line, so a value follows its own label rather than aligning to a shared column, as in the reference. The colon after each label is added in CSS, so the markup carries only the label text. Text colors keep the AA-safe tokens rather than the reference Gray-400, which reaches only 2.3:1 on white.

### Exactly reproduced values

At the project owner's direction on 2026-08-13, the sidebar (`2:164`) and the breadcrumb block (`2:263`) reproduce the Figma font weights, type sizes and geometry literally rather than through the shell's own tokens. Two of the colors were withdrawn on 2026-08-21 and now use the accessible tokens; see **Withdrawn deviation** below.

| Element | Value |
|---|---|
| Sidebar and page background | Gray-100 `#F8F9FA` |
| Nav item card / icon tiles | White, 15px and 12px radius, `0 3.5px 5.5px rgba(0,0,0,.02)` |
| Icon glyphs | Teal-300 `#4FD1C5`; white on the active Teal-300 tile |
| Nav labels | 12px bold, Gray-600 `#4A5568` (was Gray-400); Gray-700 `#2D3748` when current |
| Section heading, brand | 12px / 14px bold, Gray-700 |
| Support card | Teal-600 (was Teal-300), white 14px bold title, white 12px regular body, white 10px bold button |
| Item geometry | 219.5x54 card, 30px tile at 16px inset, label at 12px gap, 54px pitch |
| Separator | 233.25x1 at x=24.5, gradient `#E0E1E2` 0 -> 1 -> 0.15625 alpha |
| Breadcrumb | 12px regular, Gray-400 ancestors, Gray-700 current page and separator |
| Page title | 26px bold Gray-700 (was 14px), 5.5px below the breadcrumb |

Two details in the Figma file were not copied literally:

- The `Pages` breadcrumb span carries a 6px font size in the file, but the Figma render draws it at the same size as the current page. The measured render (12px) was reproduced instead.
- The sidebar frame starts 18px from the page edge and runs to the end of the column, so the shell applies that inset as left padding only.

## Tokens and accessible overrides

`wwwroot/css/tokens.css` holds the design tokens. Four values deviate from the Figma file because the original fails the WCAG 2.1 AA contrast requirement recorded in `UI_Direction.md` section 6:

| Figma value | Contrast on white | Applied value | Contrast | Used for |
|---|---|---|---|---|
| Gray-400 `#A0AEC0` | 2.26:1 | Gray-600 `#4A5568` | 7.5:1 | Secondary text, nav labels, breadcrumb links |
| Teal-300 `#4FD1C5` with white content | 1.87:1 | Teal-600 `#2C7A7B` | 5.0:1 | Icon tiles and the support card |
| Green-400 / Orange-300 / Red-500 status text | 2.4:1 – 4.1:1 | Green-700 / Orange-600 / Red-600 | 4.6:1 – 6.7:1 | Status and message text |
| 12–14px type baseline | — | 16px root, 0.625–1.5rem scale | — | Body text and 200% zoom |

These overrides apply to page content: card text, status messages, validation and error states.

**Withdrawn deviation.** These overrides were previously *not* applied to the sidebar, which reproduced Figma node 2:164 exactly at the project owner's direction on 2026-08-13. That deviation was withdrawn on 2026-08-21 after the UI/UX audit measured the result in the browser and the project owner accepted the finding. The two values it exempted, and what replaced them:

| Element | Was | Measured | Now | Measured |
|---|---|---|---|---|
| Inactive nav labels | Gray-400 on Gray-100 | 2.14:1 | Gray-600 on Gray-100 | 7.14:1 |
| Support-card title and body | White on Teal-300 | 1.87:1 | White on Teal-600 | 5.03:1 |

Both now clear the 4.5:1 AA floor. The sidebar keeps the reference geometry and the Teal-300 icon accent, which is decorative and paired with a text label in every case.

Three further contrast failures were found and fixed in the same pass. All measured figures come from `getComputedStyle` in Chromium against each element's own resolved background, not from the stylesheet source:

| Element | Was | Measured | Now | Measured |
|---|---|---|---|---|
| Table column headings | Gray-400, 10px | 2.26:1 | Gray-600, 12px bold | 7.53:1 |
| Secondary cell text | Gray-500 | 4.02:1 | Gray-600 | 7.53:1 |
| Success badge label | White on Green-400 | 2.43:1 | White on Green-700 | 6.73:1 |

The badge row above was later withdrawn by the project owner. The status pill now follows Figma node 1633:352 as drawn: a solid fill with a centred white 13px label and no silhouette. Only the informational fill clears AA; the rest do not.

| Pill | Fill | White label measured |
|---|---|---|
| Healthy | `#48BB78` | 2.43:1 |
| Critical | `#F73434` | 3.82:1 |
| Warning | `#EDD600` | 1.48:1 |
| Acknowledged | `#AB61D0` | 3.92:1 |
| High | `#FFB100` | 1.82:1 |
| Disabled, Unknown | `#919191` | 3.15:1 |
| Not yet checked | `#0030DF` | 8.44:1 |

The pill still satisfies WCAG 2.1 1.4.1 because it always prints its status in words, which is what the removed silhouette was duplicating. It does not satisfy 1.4.3 on six of the seven fills. Restoring compliance without changing the shape means either darkening the fills or switching the label to Gray-700 on the light ones.

The column-heading rule was the root cause of a second defect. `.data-table th` was unscoped, so it also matched the row headers in `tbody`: every endpoint URL rendered as 10px uppercase Gray-400. Scoping the rule to `thead th` and giving `tbody th` the body's own treatment fixed the heading contrast, the row-header contrast, and the shouted URLs together.

Every affected element still carries a non-color cue: the current page is marked with `aria-current`, planned entries carry a text badge, and every status pill names its status in words.

## Structure

| Path | Responsibility |
|---|---|
| `Views/Shared/_Layout.cshtml` | Page frame, skip link, landmarks, header, footer, message regions |
| `Views/Shared/_Sidebar.cshtml` | Brand, primary navigation, support card, drawer close control |
| `Views/Shared/_Breadcrumbs.cshtml` | Breadcrumb trail; the last entry is the current page and is never a link |
| `Views/Shared/_FlashMessages.cshtml` | One-time messages read from temp data |
| `Views/Shared/_ValidationSummary.cshtml` | Server-side model-state summary |
| `Views/Shared/_EmptyState.cshtml` | No-data and no-results state |
| `Views/Shared/Error.cshtml` | Error state for 403, 404, 409, 503 and unexpected failures |
| `Views/Shared/_Icon.cshtml` | Application-owned inline SVG icon set |
| `Shell/*.cs` | Navigation definition, breadcrumb and flash models, typed view-data helpers |
| `wwwroot/css/{tokens,shell,components}.css` | Tokens, shell layout, reusable components |
| `wwwroot/js/shell.js` | Progressive enhancement for the responsive navigation drawer and validation focus |

Bootstrap, jQuery and the template `site.css` / `site.js` are no longer referenced; the shell is application-owned CSS. The unused vendor folders under `wwwroot/lib` are left in place for a Phase 2 decision on unobtrusive client-side validation.

## Using the shell from a view

```cshtml
@{
    ViewData.SetTitle("Endpoints");
    ViewData.SetSubtitle("Optional supporting text.");
    ViewData.SetBreadcrumbs(
        new BreadcrumbItem("Registry", Url.Action("Index", "Registry")),
        new BreadcrumbItem("Endpoints"));
}

@section PageActions { <a class="button button--primary" href="...">Create</a> }
```

The page heading also becomes the document title. Flash messages are queued from a controller and survive one redirect:

```csharp
TempData.AddFlashMessage(FlashLevel.Success, "Endpoint saved.");
return RedirectToAction(nameof(Index));
```

Flash text, breadcrumb labels and validation messages are output-encoded by Razor. Never place secrets, response bodies or exception details in them.

## Navigation

`Shell/ShellNavigation.cs` defines the groups from `UI_Direction.md` section 4. Entries without a controller and action render as non-interactive items with a visible **Planned** badge and `aria-disabled="true"`, so the shell never produces an empty link to an unimplemented destination.

Navigation is a convenience, not an authorization boundary. Role-dependent visibility arrives with ASP.NET Core Identity in Phase 2 and never replaces server-side authorization of the request itself.

The Figma header's search field, account entry and notification control are rendered as operable placeholders: real focusable controls with 44px hit areas, but no behavior yet. Each states its status for assistive technology (for example "Notifications — available in Phase 4"), and the search field carries a description saying results arrive with the registry in Phase 2. They are enlarged from the 12px reference at the project owner's direction so they are legible and comfortably clickable. `@section PageActions` remains the extension point for page-level actions.

## Accessibility contract

- Skip link to `#main-content`, and `navigation`, `banner`, `main` and `contentinfo` landmarks with a single page `h1`.
- One consistent focus ring on every interactive element via `:focus-visible`.
- Below 62em the sidebar becomes an overlay drawer. While open it is announced as a modal dialog (`role="dialog"`, `aria-modal="true"`), the rest of the application is made `inert`, focus is contained, Escape closes it and focus returns to the toggle. Every attribute is removed on close, leaving a plain navigation landmark on wide viewports.
- Without JavaScript the sidebar stays in the document flow at every viewport and the drawer toggle is hidden, so navigation never depends on scripting.
- Status and severity are carried by text, never by color alone: flash messages show their level as text, status pills name their status, planned navigation entries show a **Planned** badge, and the error state shows the status code.
- A failed submission renders the validation summary above the page content, with `role="alert"`; focus moves to it when scripting is available.
- Transitions and animations are removed under `prefers-reduced-motion: reduce`.

## Error states

`ErrorViewModel.Create` maps 403, 404, 409 and 503 to dedicated safe titles and messages, and every other status to a generic message. Each state shows the correlation reference and a safe next action. A retry action is offered only for a dependency-unavailable (503) response and only when the re-executed original path is a verified local URL; no other state offers retry.

## Verification

Automated coverage lives in [`../../tests/WebHealth.IntegrationTests/ApplicationShellTests.cs`](../../tests/WebHealth.IntegrationTests/ApplicationShellTests.cs):

- the placeholder dashboard renders the skip link, landmarks, breadcrumb trail and page heading;
- the current navigation entry is marked `aria-current="page"`, planned entries are not links, and no empty `href` is emitted;
- every stylesheet, script and icon the layout references is served successfully;
- a flash message survives one redirect and is shown exactly once;
- the validation summary renders model errors above the page content;
- the empty state renders its text;
- the sidebar support artwork is served as `image/png`;
- error pages use the shell and the error-state component, the dependency-unavailable state offers a local retry target, and no other error state does.

Manual verification on 2026-08-13 in Chromium at 1440px and 414px confirmed the rendered shell against the Figma reference, the drawer's dialog semantics and background `inert` state, focus return on Escape, and that the layout does not scroll horizontally at 414px.

Measured against the Figma coordinates, the rendered sidebar reproduces the brand mark at x=42, the separator at x=24.5 (233.5px wide), the active item at x=31.5 (219.5x54), its icon tile at x=47.5 (30x30), the label at x=89.5, and the support card at x=35 (218x169.6). The breadcrumb trail and the brand lockup both start at y=44, so they sit on one line.

## Known limitations

- No Content Security Policy is defined yet; `UI_Direction.md` section 7 assigns it to later hardening work. The shell already avoids inline scripts and inline event handlers.
- The support card links to `/health/ready`, which is the only operator destination that exists in this build.
- Colors are defined for the light theme only. A dark theme is not part of the recorded scope.
- Automated accessibility auditing (axe or Lighthouse) is not wired into the delivery pipeline; Phase 5 owns dashboard accessibility checks.
