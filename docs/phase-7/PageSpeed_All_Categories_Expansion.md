# PageSpeed All-Categories Expansion

## Scope

Expand the existing PageSpeed Insights integration from Lighthouse SEO-only auditing to the four PageSpeed categories:

- Performance
- Accessibility
- Best Practices
- SEO

Each category keeps an independent mobile and desktop target, run history, score comparison, and normalized audit-item list. Existing SEO history remains valid.

## Rules and acceptance criteria

| ID | Requirement |
|---|---|
| PAC-01 | Enabling PageSpeed creates and maintains targets for every supported category and both strategies. |
| PAC-02 | A manual PageSpeed action queues every supported category for mobile and desktop. |
| PAC-03 | Every provider request explicitly sends its stored category, strategy, and locale. |
| PAC-04 | The provider accepts only the requested category score and audit references. |
| PAC-05 | Performance results include Lighthouse metric audits such as First Contentful Paint, Largest Contentful Paint, Total Blocking Time, Speed Index, and Cumulative Layout Shift when the provider references them. |
| PAC-06 | The PageSpeed page shows the latest score for all four categories for the selected strategy. |
| PAC-07 | Selecting a category shows only that category's run, comparison, counts, history, and subsection metrics. |
| PAC-08 | Existing endpoint visibility and run-now authorization policies continue to apply server-side. |
| PAC-09 | Existing SEO targets and run history are preserved by the migration. |
| PAC-10 | Provider failures, bounded responses, queue isolation, leases, idempotency, and safe diagnostics retain their existing behavior. |

## Behavior

The category query value is normalized to Performance, Accessibility, Best Practices, or SEO. Unknown values default to Performance. The strategy remains Mobile or Desktop. Category score cards navigate within the same endpoint and strategy.

PageSpeed configuration remains one endpoint-level consent and cadence. Saving it applies the same values to all eight category and strategy target rows. Disabling the feature retains all target and run history.

## Data and migration impact

The existing category columns remain the source of category identity. Their check constraints expand to the four supported values. The migration inserts missing category and strategy target rows for configured endpoints by copying the existing SEO configuration. No existing run or audit-item row is rewritten.

## Security and privacy

No new input URL is accepted. Provider requests continue to use stored eligible endpoint URLs, a fixed Google origin, bounded response parsing, an externally configured API key, and safe diagnostics. More provider requests are made per full manual or scheduled cycle, but no additional provider payload fields are stored.

## Tests and evidence

Unit and integration coverage must prove category normalization, category-specific request parameters and response parsing, target expansion, per-category read isolation, authorization preservation, and rendering of all category score cards plus performance metrics. The database foundation suite remains the migration and constraint gate.

## Operations and compatibility

One full endpoint audit now consists of eight independently queued PageSpeed requests. Existing dedicated PageSpeed workers, retry bounds, and scheduling cadence remain unchanged. Logs retain run, endpoint, provider status, and failure category without API keys or full request URIs.
