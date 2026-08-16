# Safe Outbound HTTP Transport

**Work item:** Phase 3 / WI-31 transport increment  
**Rules:** BR-H03, BR-H06, BR-H07, BR-H10, BR-Q01, BR-Q02, BR-Q04, BR-Q07  
**Acceptance criteria contribution:** AC-05 transport termination only; persistence is deferred to the result/history increment.

## Delivered boundary

`ISafeHttpTransport` is the single application-facing HTTP monitoring boundary. It accepts an endpoint ID, configured URL, and production flag, then returns a typed, bounded observation. It does not schedule work, update health, write history, classify accepted status codes, evaluate content markers, or persist response bodies.

The transport:

- uses the named `IHttpClientFactory` client `MonitoringSafeHttp` and a `SocketsHttpHandler` owned by the application;
- reuses the central URL normalizer, rejecting relative URLs, unsupported schemes, credentials, fragments, IPv6 zone identifiers, and URLs over 2,048 characters;
- disables automatic redirects, environment/system proxies, and cookies;
- sends only a GET request with the configured user-agent and never accepts arbitrary endpoint headers;
- resolves A/AAAA records inside `ConnectCallback`, rejects empty, excessive, mixed allowed/prohibited answer sets, and selects one validated address;
- connects directly to that address and verifies the actual remote address and port while retaining the original hostname for Host, SNI, and certificate validation;
- disables connection reuse for checks so DNS and destination policy run again for every request;
- authorizes the exact endpoint, normalized host, and port against active target-authorization evidence before every hop;
- rejects production HTTPS-to-HTTP redirects, normalized redirect loops, unsupported redirect targets, and chains beyond ten hops;
- retains normal platform TLS certificate and hostname validation;
- applies a 15-second whole-check timeout, 5-second connect timeout, 32 KiB header limit, 2 MiB decoded-body limit plus one sentinel byte, 16-answer DNS limit, and 20 global / 2 per-host / 4 per-IP concurrency limits;
- performs no transport-level retry; later durable execution policy owns retry decisions.

Only status, total duration, bytes actually read, truncation state, final scheme/host/port, a query-free redirect summary, and the bounded in-memory body are returned. The transport logs and persists none of these values. Result normalization will consume the body in memory and persistence will retain zero body bytes by default.

## Destination policy

Normal runtime registration uses `StrictDestinationAddressPolicy`. It rejects loopback, unspecified, private, shared, link-local, metadata, documentation, benchmarking, multicast, reserved, IPv4-mapped special, NAT64, discard-only, IETF special-purpose, 6to4, unique-local, site-local, and other recorded special-purpose IPv4/IPv6 ranges.

Loopback is available only through the test-injected policy used by controlled TCP/TLS fixtures. There is no application setting that disables TLS validation, enables implicit proxying, or permits loopback in normal runtime configuration.

## Authorization and error behavior

The initial target and every redirect require current `target_authorization` evidence for the same endpoint and exact normalized host/port. An unauthorized redirect is rejected before DNS resolution or connection. Expired and revoked evidence fails closed.

Expected transport failures return a stable category: invalid URL, unauthorized target, prohibited destination, name resolution, connection, TLS, timeout, caller cancellation, oversized headers, malformed/missing redirect location, loop, hop limit, production downgrade, or protocol failure. Raw exception messages, headers, cookies, query values, credentials, and response content are not included in the failure contract.

## Verification evidence

- Unit coverage exercises public and prohibited IPv4/IPv6 boundaries, including mapped and translated forms.
- Controlled TCP tests verify Host/user-agent preservation, bounded decoded bodies, mixed-answer rejection before contact, DNS rebinding rejection, per-hop authorization, loop and hop-limit termination, invalid redirects, oversized headers, timeout, caller cancellation, and concurrency queues.
- A controlled TLS fixture proves an invalid certificate remains a TLS failure.
- Handler configuration tests prove automatic redirects, proxies, cookies, and certificate-validation overrides are disabled. The Phase 0 feasibility spike remains the lower-level evidence for IPv4/IPv6 socket pinning and no-proxy behavior.
- No database migration is required for this increment; it consumes Phase 2 target-authorization evidence.

## Remaining work

The next increment owns normalized HTTP results, accepted-status and marker evaluation, detailed safe timing availability, terminal result persistence, and zero-body history. Scheduling, Hangfire execution, durable retries, and monitoring UI remain later Phase 3 increments.
