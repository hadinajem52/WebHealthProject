# Security and Safe-Monitoring Design

**Owner:** Intern  
**Scope:** Personal local/demo project; production operations are deferred
**Approval:** Approved by the intern/project owner on 2026-08-13

## 1. Trust boundaries and principal threats

Untrusted inputs include browser requests, configured URLs, DNS answers, redirect locations, target headers/bodies/HTML, certificates, SMTP responses, and optional proxy behavior.

Protect:

- Accounts, sessions, roles, and assignments.
- Database/SMTP credentials and Figma-derived application assets.
- Registry, checks, incidents, notifications, and audit history.
- The machine's outbound network access.
- Availability of the monitor and websites being tested.

| Threat | Foundation control | Required test |
|---|---|---|
| Unauthorized direct requests | Server-side role/assignment policies; deny by default | Every application role and assignment combination |
| CSRF/XSS | Anti-forgery on browser mutations; output encoding | Missing token and markup payload tests |
| SSRF/DNS rebinding | Public-address policy, resolution in connection path, pinned IP, peer verification | IPv4/IPv6, mixed DNS, rebinding, redirect tests |
| Redirect escape | Manual per-hop validation, loop detection, 10-hop limit | Private/cross-host/downgrade/loop cases |
| TLS bypass | Normal platform validation remains authoritative | Invalid certificates stay failed while evidence is captured |
| Resource exhaustion | Deadlines, streaming, body/header/concurrency/rate limits | Slow, large, chunked, compressed, cancellation cases |
| Duplicate effects | Stable IDs, leases, transactions, PostgreSQL uniqueness | Competing workers, retry, restart |
| Secret/diagnostic leak | Local secret storage and allow-listed logs/data | Canary-value scans |
| Unauthorized target testing | Only targets owned by the intern or explicitly permitted | Enablement/redirect authorization checks |

## 2. Destination-network policy

Default: monitor only personally owned or explicitly permitted hosts that resolve to public global-unicast addresses. Deny takes precedence over allow. Allow only HTTP/HTTPS and ports 80/443 unless the intern records a specific test exception.

Reject URL credentials, unsupported schemes, IPv6 zone IDs, and ambiguous address forms. Normalize IPv4-mapped IPv6 before classification.

### Prohibited IPv4

At minimum deny unspecified/current-network, private, shared-CGNAT, loopback, link-local/metadata, protocol-assignment, documentation, benchmarking, multicast, reserved, and broadcast ranges, including:

`0/8`, `10/8`, `100.64/10`, `127/8`, `169.254/16`, `172.16/12`, `192.0.0/24`, `192.0.2/24`, `192.168/16`, `198.18/15`, `198.51.100/24`, `203.0.113/24`, `224/4`, and `240/4`.

### Prohibited IPv6

At minimum deny unspecified, loopback, IPv4-embedded special forms, NAT64 special ranges, discard-only, special-purpose/documentation, 6to4, unique-local, link/site-local, and multicast ranges, including:

`::/128`, `::1/128`, `::/96`, `64:ff9b::/96`, `64:ff9b:1::/48`, `100::/64`, `2001::/23`, `2001:db8::/32`, `2002::/16`, `3fff::/20`, `fc00::/7`, `fe80::/10`, `fec0::/10`, and `ff00::/8`.

Use a maintained IANA special-purpose registry snapshot in addition to these regression cases.

### Personal private-network exceptions

Private monitoring is off by default. A local-development exception must specify exact host/address/port, purpose, and expiry and must never allow loopback, metadata, unspecified, or multicast targets through ordinary user configuration. Test-only loopback fixtures use a separate injected policy unavailable in normal runtime configuration.

## 3. Safe HTTP transport

- One application-owned transport through `IHttpClientFactory` and `SocketsHttpHandler`.
- Automatic redirects and implicit proxies disabled.
- Resolve A/AAAA inside `ConnectCallback` through an injectable resolver.
- Reject mixed allowed/prohibited answer sets.
- Connect directly to one validated IP, verify `RemoteEndPoint`, and preserve the original hostname for Host/SNI/certificate validation.
- Repeat the full policy for every new connection and redirect.
- Never forward cookies, Authorization, Proxy-Authorization, or arbitrary endpoint headers across redirects.
- Reject HTTPS-to-HTTP downgrade for production-labelled endpoints.
- Cross-host redirects require explicit permission.
- No generic alternate `HttpClient` may bypass this transport.

If a future deployment requires an outbound proxy, its ability to enforce the actual origin must be designed and tested in that deployment phase. It is not a Phase 0 requirement for local/demo use.

## 4. Initial bounds

| Limit | Initial value |
|---|---:|
| Whole HTTP check | 15 seconds |
| Connect portion | 5 seconds within total |
| Redirects | 10 |
| Headers | 32 KiB |
| Decoded body read | 2 MiB plus sentinel byte |
| Persisted body | 0 bytes by default |
| URL | 2,048 characters |
| DNS answers | 16 |
| Initial global checks | 20 |
| Per-host / per-IP checks | 2 / 4 |
| Crawler | Specification page/depth/rate defaults; detailed total budgets in Phase 6 |

No automatic HTTP retries occur inside the transport; durable job policy owns retry decisions.

## 5. Data and secret safety

Logs may contain stable IDs, event/outcome categories, status, timing, byte/hop/retry counts, and sanitized exception types. They must not contain credentials, connection strings, cookies, authorization headers, body/HTML content, query values, recipient addresses, raw SMTP responses, or generic object dumps.

Persist only normalized failure categories, timings, lengths, redacted redirect information, bounded certificate evidence, and parser/policy outcomes.

For local development, use ASP.NET Core user-secrets or an ignored local settings file. CI uses protected secret variables if CI is configured. Never commit Gmail app passwords, database passwords, license keys, or private certificates.

## 6. Application security

- Authenticate before operational data.
- Authorize in controllers/application services and constrain data queries; hiding controls is insufficient.
- Require anti-forgery for state-changing MVC requests.
- Encode labels, notes, URLs, certificate fields, and diagnostics.
- Use a configured canonical application URL for generated links.
- Public liveness is minimal; detailed diagnostics and Hangfire administration require authorization.
- If deployed behind a reverse proxy later, configure exact trusted proxies and test forged forwarded headers.

## 7. Later-phase operational notes

For a personal local/demo project, production hosting, HA, RPO/RTO, PITR, enterprise vaults, centralized logging/alerting, on-call ownership, production restore drills, and formal release governance are deferred. If public or business-critical deployment is later attempted, those become mandatory design and verification work before claiming production readiness.

## 8. Immediate proof required

Phase 0 focuses on:

1. Address classifier and URL validation tests.
2. Actual-connection pinning and peer verification.
3. IPv4/IPv6 redirect and DNS-rebinding fixtures.
4. TLS evidence without validation bypass.
5. No-proxy behavior.

Crawler isolation/load, production Gmail, backup/restore, retention interruption, and deployment hardening are tested in their owning phases.
