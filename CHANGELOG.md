# Changelog

All notable changes are documented here. TradeRelay uses semantic versioning; milestone tags before `1.0.0` are development releases.

## [1.1.0] - 2026-07-13

### Added

- Responsive Operations Rail with persistent exchange, environment, service-health, audit-health, and trading-state context.
- Provider-neutral exchange IDs, profile keys, capabilities, credential-field descriptors, registry, and session-coordinator foundation.
- Redacted multi-field credential sets and protected payload storage ready for passphrase-based providers.

### Changed

- Moved credential and agent setup into Connections and repositioned Demo as an onboarding environment rather than the product identity.
- Migrated legacy Bybit settings to provider profiles while retaining existing protected `bybit:demo` and `bybit:live` identifiers.

### Security

- Preserved the single Bybit write path and its existing startup disable, gate, approval, audit, lease, and reconciliation controls.

## [1.0.0] - 2026-07-13

### Added

- Production Settings page with MCP port validation, automatic startup, token rotation, folder actions, and sanitized diagnostics export.
- Shared sensitive-data redaction, serialized daily safe logs, bounded error memory, and atomic non-secret diagnostics.
- Reproducible icons, portable packages for six operating-system/architecture targets, checksums, dependency and release manifests, signing hooks, native smoke tests, and provenance attestations.
- Locked restores, cross-platform CI, CodeQL, dependency review, Dependabot, Demo-only integration workflow, open-source policy files, and complete operator/developer documentation.

### Security

- Preserved session-disabled trading startup, centralized write gates, immutable approvals, Live confirmation boundaries, audit requirements, reconciliation, and the prohibition on automated Live writes.

## [0.6.0] - 2026-07-12

- Added guarded Bybit Live trading safety, connection-bound plans, market-drift checks, atomic write leases, and short-lived destructive-action confirmations.

## [0.5.0] - 2026-07-11

- Added explicitly enabled Bybit Demo execution, reconciliation, cancellation/close/stop actions, and append-only Activity auditing.

## [0.4.0] - 2026-07-11

- Added risk calculation, immutable prepared simulations, expiration, and desktop approvals.

## [0.3.0] - 2026-07-10

- Added protected credential management and Bybit read-only market/account connectivity.

## [0.2.0] - 2026-07-10

- Added the local authenticated MCP host and desktop control panel.

## [0.1.0] - 2026-07-09

- Added the initial solution and corrected the desktop project naming.
