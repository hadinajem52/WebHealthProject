# Monitoring status reference

Confirmed target health uses the shared status badge: Healthy is success, Warning is warning,
Critical is danger, and Unknown is neutral. Legacy stored Disabled health displays as Unknown.
Pausing or disabling monitoring preserves the last confirmed health.

Operational state appears as separate text on dashboard monitor rows and endpoint details. Its
precedence is Disabled, Manual only, Paused, Delayed, Never checked, Stale, Active. Disabled means
the endpoint or a parent is inactive or archived; Manual only means scheduling is disabled;
Paused means the individual monitor is disabled. These states do not imply target failure.

Delayed means an active monitor is past its next due time by more than DispatchDelayGrace
(default ten minutes, configurable from two through thirty minutes). Freshness uses only the
latest completed current-generation scheduled check. Manual, urgent and superseded results do
not refresh it. Stale starts after one interval plus the greater of ten minutes or 25 percent
of that interval. Never checked means no qualifying scheduled completion exists.

The dashboard monitoring-system card and protected diagnostics page show engine health using
the same Healthy, Warning and Critical tones. Engine health evaluates scheduler, worker and
backlog evidence separately from target health. Disabled scheduling displays Healthy with
DisabledByConfiguration; it does not claim that any monitored target is healthy. Database
readiness remains separate. Detailed diagnostics are limited to Administrator and Operations.

CSV ConfirmedStatus contains health. OperationalState and LastScheduledCompletionAt are
separate columns. Historical raw outcomes remain evidence of their original checks.

Verification and local-demo limitations are recorded in
[the monitoring evidence](../phase-7/Monitoring_Hardening_Evidence.md). Worker and scheduler
state is global; selected registry filters affect aggregate monitor and work facts. No external
metrics exporter or production availability guarantee is part of this implementation.
