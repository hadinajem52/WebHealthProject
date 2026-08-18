# Certificate Capture and Safe TLS Inspection

**Work item:** Phase 5 / increment 5.1  
**Rules:** BR-C01, BR-C02, BR-C03, BR-Q01, BR-Q02, BR-Q04  
**Acceptance criteria contribution:** AC-06 evidence capture only. Severity boundaries, expiry deduplication, renewal, persistence, and scheduling are later Phase 5 increments.

## Delivered boundary

This increment adds the ability to observe an endpoint's TLS certificate. It adds no schema, no scheduling, no incident behavior, and no UI. Two separate paths produce certificate evidence, and the separation is deliberate.

**It is a foundation increment with no consumer yet.** Nothing reads `SafeHttpTransportResult.Certificate` or calls `ISslCertificateProbe` in production code; both exist to be consumed by increments 5.2 and 5.3. The Phase 3 statement that certificate evidence is outside the transport contract was accurate when written and is superseded by this document; [the Phase 3 transport evidence](../phase-3/Safe_Outbound_HTTP_Transport.md) now points here. Phase 3's gate is unaffected — no Phase 3 behavior changed.

### Availability path — record what was already accepted

`SafeHttpConnectionFactory` captures the negotiated leaf certificate inside the existing `PlaintextStreamFilter`, which runs only after the platform has completed a fully validated handshake. The encoded certificate is copied into a per-attempt sink attached to the request options and surfaced as `SafeHttpTransportResult.Certificate` for the terminal (non-redirect) response.

Nothing about validation changes. The handler still has no `RemoteCertificateValidationCallback`, and the existing handler-configuration test continues to assert that. A certificate recorded on this path is by definition trusted, hostname-matched and inside its validity window, because no HTTP response could exist otherwise — so it is classified as `Valid` without re-deriving what the platform already proved.

Certificates are recorded only for HTTPS. HTTP-only endpoints yield `Certificate == null`, which is the input for the Not Applicable display BR-C01 requires.

### SSL monitor path — record what must be rejected

`ISslCertificateProbe` opens a TLS connection for the sole purpose of inspecting the certificate. Its `RemoteCertificateValidationCallback` records the presented certificate and the platform's `SslPolicyErrors`/chain status, and then **always returns `false`**.

**This is the decision a reviewer should scrutinise, so it is recorded explicitly.** BR-C03 requires reporting expired, not-yet-valid, hostname-mismatched and untrusted certificates. Those certificates are, by definition, ones the platform refuses. Evidence about them cannot be obtained without a validation callback that sees them. The safety property is preserved by what the callback returns, not by whether it exists:

- the callback never returns `true`, for any input, under any configuration — there is no setting, flag, or environment that makes it accept;
- because the handshake always fails, no session key is ever put to use;
- no application data is ever written to or read from a probe connection, which is asserted by a test that watches the server side;
- the probe is a separate type from the availability transport, so this behavior can never leak into a path that fetches content. BR-Q04 remains intact: the monitoring HTTP client still validates certificates normally and still has no override.

The probe reuses the same safety machinery as availability checks rather than reimplementing it. `SafeDestinationConnector` was extracted from the HTTP handler's `ConnectCallback` in this increment and is now the single implementation of resolve → policy-check every answer → connect → verify the address actually connected to. Both paths call it, so destination policy, DNS rebinding protection and actual-connection enforcement (BR-Q01, BR-Q02) cannot drift apart. The probe also applies target-authorization evidence and the same global/host/address concurrency limits before connecting.

## Recorded evidence and classification

`TlsCertificateObservation` carries what BR-C02 requires — subject, issuer, serial number, SHA-256 fingerprint, valid-from, valid-to, subject alternative names, the hostname/trust signals, the category and the observation instant. Names are truncated at 512 characters and SANs capped at 20 entries. No private key material and no raw certificate bytes are retained. Remaining days and severity are deliberately absent; they belong to increment 5.3.

`TlsCertificateEvaluator.Classify` is a pure domain function with a documented precedence: **not-yet-valid → expired → hostname mismatch → untrusted → valid**. Time validity wins because it is the condition this system monitors and renews against, and because an expired certificate normally reports chain errors too — labelling it merely "untrusted" would hide the actionable cause. All underlying signals are stored alongside the category, so precedence only decides which label leads, never which evidence survives. The validity window is inclusive at both ends, matching RFC 5280.

Time-validity chain statuses are excluded from the trust decision for the same reason, so `ChainTrusted` describes trust alone — but **only for the leaf**. `TlsChainTrust.Evaluate` works from per-element status flags rather than the aggregate `X509Chain.ChainStatus`, because the aggregate cannot say which element a failure came from. Forgiving time validity across the whole chain would report a genuinely broken chain as `Valid` whenever an intermediate or root had expired but the leaf's own dates happened to be fine. Every non-leaf element must be completely error-free; an expired issuer is reported as untrusted, never as valid.

A handshake that never produced a certificate has no category. It is reported as `SslProbeFailureKind.HandshakeFailed`, which BR-C03 treats as critical alongside the invalid-certificate categories. Failure classification is by phase, not by exception type: once the socket is connected, anything that goes wrong belongs to the handshake, because platforms surface a refused handshake as an authentication error in one case and a dropped stream in another. Connect-level problems remain `Connection`/`NameResolution`, and cancellation and timeout keep their own meanings.

## Verification evidence

Unit tests (`TlsCertificateEvaluatorTests`, 7 cases) cover the precedence order and both inclusive validity-window boundaries to the tick.

`TlsChainTrustTests` (8 cases) covers the trust rule directly against per-element flags, including the regression case for an expired intermediate and an expired root. The rule is tested at that level rather than through a manufactured PKI because which element a failure came from is precisely what the flags express, and because the probe deliberately offers no hook for injecting a custom trust anchor.

Controlled-TLS integration tests (`SslCertificateProbeTests`, 14 cases) extend the SP-03 fixtures with generated certificates and prove:

- full BR-C02 evidence is recorded for a certificate inside its validity window, with the fingerprint matching the served certificate;
- expired, not-yet-valid and hostname-mismatched certificates each produce their own category, with observed SANs;
- a refused handshake reports `HandshakeFailed` with no certificate, a dropped connection reports `HandshakeFailed`, and a closed port reports `Connection`;
- destination policy and target authorization are both applied *before* the target is contacted (the fixture records zero contacts);
- an HTTP-only URL is rejected as `NotHttps` without connecting;
- caller cancellation and probe timeout are reported separately;
- **no application data is ever sent over a probe connection.** Under TLS 1.3 the server may consider its own side complete before the client's rejection alert arrives, so the server-side handshake outcome proves nothing; the test asserts the absence of application bytes instead.

Availability-path capture is covered in `SafeHttpTransportTests` by a genuinely validated handshake: a throwaway test CA issues a server certificate, and the test installs that root through `SslOptions.CertificateChainPolicy` (`CustomRootTrust`) on that one handler. Validation is not weakened — expiry, hostname and signature are all still checked by the platform, and no machine certificate store is touched. The test asserts the captured subject, issuer, fingerprint, SANs and `Valid` category. A companion assertion proves a plain-HTTP response records no certificate.

Full local delivery run on 2026-08-18: 119 unit and 115 integration tests passing, warning-free build. The three skipped integration tests are the pre-existing opt-in PostgreSQL and SMTP gates. No migration is required by this increment.

## Remaining work

Increment 5.2 adds the `SslCertificate` monitor type, the `certificate_observation` table, daily scheduling, and the urgent re-check after a TLS-related HTTP failure (BR-C07). Increment 5.3 adds days-remaining, the 30/15/7 severity boundaries, fingerprint-based expiry deduplication (BR-C05) and renewal resolution (BR-C06).
