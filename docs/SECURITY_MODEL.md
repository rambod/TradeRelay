# TradeRelay Security Model

TradeRelay is a local control plane for exchange access. It reduces accidental or unauthorized writes, but it cannot remove market, exchange, credential, or operator risk.

## Local boundary and secrets

- The MCP server binds only to `127.0.0.1` and requires a cryptographically random bearer token.
- API credentials and the MCP token use operating-system protected storage when available and otherwise remain session-only.
- Credentials, tokens, authorization headers, signatures, and raw authenticated payloads are excluded from MCP results, settings, logs, and JSONL audit events.
- A shared redactor protects audit, safe logs, and diagnostics. Daily safe logs contain fixed messages, normalized properties, and exception types only—never exception messages or stack traces.
- Diagnostics exports contain non-secret runtime health, risk settings, bounded safe errors, and package versions. They exclude raw audit entries and log files and receive a final forbidden-field/value scan before atomic write.
- API keys with withdrawal permission are rejected. Read/write, unbound, master-account, broad-permission, and expiring keys are shown as explicit warnings.

## MCP authentication and client scopes

The preferred authentication flow uses loopback OAuth discovery, dynamic registration, Authorization Code with PKCE-S256, exact redirect and state binding, short-lived access tokens, rotating protected refresh-token hashes, and revocation. Pairing requests expire after five minutes, codes after 60 seconds, access tokens after 15 minutes, and refresh credentials after 30 inactive days.

Scopes are enforced at the MCP boundary: `traderelay.read` for inspection, `traderelay.plan` for risk and preparation, and `traderelay.trade` for writes. New pairings default to Read & Plan. Trade requires explicit desktop approval and cannot bypass environment gating, trading enablement, plan/action approval, write leases, mandatory audit, or reconciliation. The in-memory legacy bearer token remains available only as Advanced compatibility.

Client installers preview exact commands and targets, pass no secrets in arguments, reject conflicting `traderelay` entries, and uninstall only files marked as TradeRelay-owned.

## Multi-exchange capability boundary

Bybit is the only adapter advertising `TradingWrite`. Binance USD-M Futures and KuCoin USDT Futures are Live read-only adapters for normalized inspection and private-stream health. All three may connect concurrently, but supplying Binance or KuCoin to a write tool returns `CAPABILITY_NOT_SUPPORTED` before a write lease or provider invocation. Their adapter-level trading surfaces also reject every operation defensively.

Credentials remain isolated by `exchange:environment` protected-storage identifiers. KuCoin's passphrase is treated as a secret field and follows the same non-serialization, redaction, and protected-storage rules as API secrets.

## Trading enablement

Every application launch begins with trading disabled. Demo and Live must be enabled separately from the desktop after the MCP server, credentials, account access, risk settings, audit storage, and reconciliation path pass readiness checks.

Live additionally requires the exact phrase `ENABLE LIVE TRADING`. Enablement applies only to the current process and matching authenticated connection. Environment changes, credential changes, provider failure, MCP stop, audit failure, emergency disable, or shutdown immediately block new writes.

`Disable New Trading Actions` does not cancel exchange orders, close positions, or remove stop-loss or take-profit protection.

## Plans, approvals, and destructive actions

- Orders originate from immutable prepared plans with caller idempotency, expiration, environment, connection generation, risk snapshot, canonical SHA-256 hash, and optional desktop approval.
- Live manual approval is enabled by default. Changing that preference affects only new plans.
- Live market orders are rejected when the current executable price differs from the approved estimate by more than the plan's stored price-deviation limit.
- Live cancel-all and close-position requests first create a 60-second immutable desktop confirmation. The MCP caller may execute only the exact approved parameters, once, in the same Live trading and provider-connection session.
- Cancel-all still requires `confirm=true`. Position closes re-fetch the current position, clamp quantity, and use reduce-only behavior.

## Central gate and reconciliation

Every exchange write enters the same centralized gate. It verifies session state, environment agreement, credentials, permissions, risk health, audit health, provider availability, plan connection binding, and required approvals before granting an atomic write lease.

Order submission is never blindly retried. REST acknowledgement is provisional: TradeRelay waits for private-stream evidence and then performs one REST reconciliation lookup. Ambiguous state is reported as `ORDER_STATE_UNKNOWN` and disables new trading actions.

## Audit and shutdown

Exchange writes require a successful pre-action audit append. Later audit failure faults audit health and blocks subsequent writes. Audit files are append-only UTF-8 JSONL under the normal per-user application-data directory.

Audit history is retained until the desktop operator explicitly deletes a UTC date range or all retained files by typing `DELETE AUDIT HISTORY` exactly. A new safe purge event is written after deletion. Exchange-returned history and TradeRelay-observed lifecycle events remain separately identified; TradeRelay does not fabricate history from before it observed an account.

Shutdown disables new writes first, expires unexecuted plans and Live confirmations, permits active reconciliation for a short bounded interval, records shutdown, disconnects streams, disposes clients, and stops the local server. It never performs automatic exchange cleanup.

Automatic MCP startup is an operator convenience only. A startup fault leaves the desktop open and trading disabled. Changing the stopped MCP port or rotating its token does not enable trading.

Release packages are reproducibly structured, checksummed, dependency-manifested, and provenance-attested. Signing is optional but its state is explicit; partially configured signing fails rather than silently producing an incorrectly labeled artifact.

## Testing policy

Normal tests use fake providers and a real loopback Kestrel/MCP client path. Optional write integration targets Bybit Demo only. The repository never automates a real Bybit Live write.
