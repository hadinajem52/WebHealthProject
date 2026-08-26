# Incident Severity and Badge Reference

Two lookups read straight out of the code: which issue each rule opens and how severe it is, and
what every badge style looks like. Both are derived, not decided here — if a value below and the
source disagree, the source is right and this file is stale.

## Incident types and their severity

An incident is exactly as severe as the finding that confirmed it (BR-C04). Severity escalates and
never de-escalates: if a later finding on the same issue key is more severe, the open incident is
raised to it and the change is written to the timeline. A confirmed issue with no finding behind it
came from a transport failure and is Critical.

The stored issue key is `v1|<monitor>|<rule>|<discriminator>`. The discriminator is the literal word
`default` for every rule except certificate expiry, which uses the certificate's SHA-256 fingerprint
so a renewal is recognisable as the same subject. Only the rule segment is listed below.

| Monitor | Rule key | Reads as | Severity |
|---|---|---|---|
| HttpAvailability | `Http.Dns` | Hostname did not resolve | Critical |
| HttpAvailability | `Http.Connection` | Connection refused or unreachable | Critical |
| HttpAvailability | `Http.Tls` | TLS negotiation failed | Critical |
| HttpAvailability | `Http.Timeout` | Request timed out | Critical |
| HttpAvailability | `Http.RedirectLoop` | Redirects formed a loop | Critical |
| HttpAvailability | `Http.ExcessiveRedirects` | Too many redirects | Critical |
| HttpAvailability | `Http.InvalidRedirect` | Redirect target was rejected | Critical |
| HttpAvailability | `Http.DestinationPolicy` | Destination is blocked by network policy | Critical |
| HttpAvailability | `Http.InvalidConfiguration` | Monitor configuration is invalid | Critical |
| HttpAvailability | `Http.Protocol` | Protocol error | Critical |
| HttpAvailability | `Http.ResponseTooLarge` | Response exceeded the size limit | Critical |
| HttpAvailability | `Http.ClientError` | Responded 4xx | Critical |
| HttpAvailability | `Http.ServerError` | Responded 5xx | Critical |
| HttpAvailability | `Http.ContentMismatch` | Required content was not found on the page | Critical |
| HttpAvailability | `Http.SlowResponse` | Slower than its response-time threshold | Warning |
| HttpAvailability | `Http.PageTooLarge` | Larger than its page-size threshold | Warning |
| HttpAvailability | `Http.HttpsRequired` | HTTPS is required but the endpoint served HTTP | The endpoint's `ProductionHttpSeverity` — Warning by default, Critical if configured |
| HttpAvailability | `Seo.RobotsBlocksSite` | robots.txt blocks the whole site | Warning |
| HttpAvailability | `Seo.RobotsBlocksEndpoint` | robots.txt blocks this page | Warning |
| HttpAvailability | `Seo.RobotsUnavailable` | robots.txt could not be read | Warning |
| HttpAvailability | `Seo.SitemapMissing` | Sitemap is missing | Warning |
| HttpAvailability | `Seo.TitleMissing` | Title is missing | Warning |
| HttpAvailability | `Seo.TitleDuplicate` | Page has more than one title | Warning |
| HttpAvailability | `Seo.DescriptionMissing` | Meta description is missing | Warning |
| HttpAvailability | `Seo.CanonicalInvalid` | Canonical URL is not a valid URL | Warning |
| HttpAvailability | `Seo.CanonicalDuplicate` | Page has more than one canonical URL | Warning |
| HttpAvailability | `Seo.CanonicalNotAbsolute` | Canonical URL is not absolute | Warning |
| HttpAvailability | `Seo.CanonicalUnexpectedHost` | Canonical URL points at another host | High on production, Warning elsewhere |
| HttpAvailability | `Seo.NoIndexUnexpected` | Page is set to noindex but should be indexable | High on production, Warning elsewhere |
| HttpAvailability | `Seo.IndexableUnexpected` | Page is indexable but should not be | High on production, Warning elsewhere |
| SslCertificate | `Ssl.Expiry` | Certificate is expiring | Critical at 7 days or fewer, High at 15 or fewer, Warning at 30 or fewer |
| SslCertificate | `SslExpired` | Certificate has expired | Critical |
| SslCertificate | `SslNotYetValid` | Certificate is not yet valid | Critical |
| SslCertificate | `SslHostnameMismatch` | Certificate does not cover the requested host | Critical |
| SslCertificate | `SslUntrusted` | Certificate is not trusted | Critical |
| SslCertificate | `SslHandshakeFailed` | TLS handshake failed | Critical |

Notes on the conditional rows:

- **Production vs elsewhere** is the endpoint's environment, and it applies only to the three
  canonical and indexing rules. Nothing in the SEO family is Critical anywhere: a misconfigured or
  uncrawlable page is not an unreachable one, and reserving Critical for availability is what keeps
  the severity vocabulary worth reading. Both robots rules are Warning in every environment.
- **Certificate expiry bands** are the BR-C04 defaults (30 / 15 / 7 days) and are configurable per
  monitor. Every boundary is inclusive on the unhealthy side: exactly 30 days remaining is already
  Warning. An expired certificate reports a negative day count and lands in the Critical band by
  the same comparison.
- **`Ssl.Expiry` is one rule for the whole expiry lifecycle**, approaching and past. The reported
  failure category still distinguishes `SslExpiringSoon` from `SslExpired`, but the key does not —
  a certificate crossing its own expiry date has not developed a second problem, and splitting the
  key there would open a duplicate incident for the same certificate.

Two categories never open an incident, because they produce a result with no findings:
`Cancellation` (a cancelled check observed nothing) and `TargetIneligible` (the target was not
eligible to be checked). `ExecutionExhausted` is a Critical result but likewise carries no finding
of its own.

## Badge styles and their colours

Every badge is a pill with white label text on a solid fill, selected by its `data-status`
attribute. The fill is the only thing that varies; each style also carries a glyph, so no state is
distinguishable by colour alone.

| `data-status` | Token | Fill | Glyph | Used for |
|---|---|---|---|---|
| `success` | `--badge-success` | `#48bb78` | circle-check | Healthy outcomes, resolved and closed incidents, a certificate outside every expiry band |
| `danger` | `--badge-danger` | `#f73434` | circle-close | Critical severity and Critical outcomes, open incidents, Critical endpoint health |
| `warning` | `--badge-warning` | `#edd600` | triangle-warning | Warning severity and Warning outcomes, in-progress and monitoring-recovery incidents, unacknowledged |
| `high` | `--badge-high` | `#ffb100` | triangle-warning | High severity — the band between Warning and Critical |
| `acknowledged` | `--badge-acknowledged` | `#ab61d0` | circle-info | Acknowledged incidents. Its own fill rather than Open's: someone has picked it up, which is not the same as nobody having looked yet |
| `info` | `--badge-info` | `#0030df` | circle-info | A check still in flight, and other neutral-but-active states |
| `neutral` | `--badge-neutral` | `#919191` | circle-info | Disabled or not yet reported, and recurrence counts. Neither a verdict nor information |

Label text is `--badge-label`, `#ffffff`, on all seven. An unrecognised `data-status` falls back to
the `neutral` fill and the circle-info glyph rather than to no styling, so a badge is never
colour-only by accident.

### How a value becomes a badge style

| Producer | Input | Mapping |
|---|---|---|
| `StatusBadges.ForSeverity` | finding or incident severity | Critical → `danger`, High → `high`, anything else → `warning` |
| `StatusBadges.ForIncidentStatus` | incident lifecycle status | Open → `danger`, Acknowledged → `acknowledged`, InProgress and MonitoringRecovery → `warning`, Resolved and Closed → `success` |
| `StatusBadges.ForOutcome` | check outcome | Healthy → `success`, Critical → `danger`, anything else → `warning` |
| `StatusBadges.ForHealthStatus` | confirmed endpoint health | Healthy → `success`, Critical → `danger`, Warning → `warning`, Unknown and Disabled → `neutral` |
| `StatusBadges.ForExpirySeverity` | certificate expiry band | Critical → `danger`, High → `high`, Warning → `warning`, None → `success` |

## Sources

| Table content | Source |
|---|---|
| Rule wording | `src/WebHealth.Web/Models/IssueDisplay.cs` |
| HTTP and performance severities | `src/WebHealth.Application/Monitoring/HttpResultNormalization.cs` |
| SEO severities | `src/WebHealth.Application/Seo/SeoRuleEvaluator.cs` |
| robots.txt severities | `src/WebHealth.Application/Seo/RobotsRuleEvaluator.cs` |
| Certificate severities and bands | `src/WebHealth.Application/Monitoring/SslResultNormalization.cs`, `src/WebHealth.Domain/Monitoring/CertificateExpiry.cs` |
| Severity vocabulary and escalation | `src/WebHealth.Domain/Incidents/IncidentLifecycle.cs`, `src/WebHealth.Infrastructure/Incidents/IncidentAutomationService.cs` |
| Badge tokens and fills | `src/WebHealth.Web/wwwroot/css/tokens.css`, `src/WebHealth.Web/wwwroot/css/components.css` |
| Tone and glyph mapping | `src/WebHealth.Web/Shell/StatusBadges.cs` |
