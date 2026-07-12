# TradeRelay

TradeRelay is a local desktop bridge intended to connect MCP-capable coding agents to controlled Bybit market-data and trading workflows.

> [!WARNING]
> TradeRelay is under active development. Milestone 5 can send explicitly enabled write requests to Bybit Demo only. Live exchange writes remain unavailable.

## Current status

Milestone 5 provides:

- A .NET 10 solution with Avalonia desktop application, core, Bybit provider-boundary, and test projects
- Dashboard and credential control panels
- A repeatedly startable and stoppable loopback-only MCP server
- Protected bearer-token storage with session fallback and token rotation
- Session-only credentials by default, with optional macOS Keychain, Windows DPAPI, or Linux Secret Service persistence
- Bybit Demo and Live credential validation with withdrawal-permission rejection
- Read-only USDT perpetual market, Unified Account, order, position, and WebSocket-health access
- MCP tools for system status, connection status, tickers, candles, instruments, order books, balances, positions, and open orders
- Instrument-aware position sizing, order normalization, and risk validation
- Expiring immutable prepared simulations with idempotent request IDs and SHA-256 hashes
- Desktop-only approval and rejection with Demo auto-approval policy
- Risk, Approvals, and execution-focused Activity views
- MCP tools for risk settings, position sizing, validation, preparation, and approval inspection
- Session-only, desktop-enabled Bybit Demo trading with centralized safety gates
- Exactly-once prepared-order submission, WebSocket/REST reconciliation, partial-fill reporting, and ambiguous-state protection
- Demo order cancellation, explicit cancel-all, reduce-only position closing, and full-position trading stops
- Redacted, append-only daily JSONL activity auditing
- Unit, Kestrel integration, security, concurrency, and optional Bybit Demo integration coverage

Live execution, persistent trading enablement, databases, and automatic exchange cleanup are intentionally not implemented. Stopping or disabling TradeRelay never cancels orders, closes positions, or removes protection.

Current version: `0.5.0`

## Credential safety

- Use Bybit Demo first and create separate Demo credentials.
- Never use an API key with withdrawal permission; TradeRelay rejects it.
- Leave **Remember credentials** disabled for session-only storage.
- When enabled, TradeRelay uses the operating system's protected credential store and never writes credentials to `settings.json`.

Optional Bybit Demo integration tests use `TRADERELAY_BYBIT_DEMO_API_KEY` and `TRADERELAY_BYBIT_DEMO_API_SECRET`. They do nothing when those variables are absent and never target Live.

Write-capable Demo integration tests additionally require an explicit `TRADERELAY_RUN_DEMO_TRADING_TESTS=1` opt-in and never run in normal test execution.

## Release roadmap

| Milestone | Version |
| --- | --- |
| 1 — Scaffold and naming correction | `0.1.0` |
| 2 — Control panel and MCP host | `0.2.0` |
| 3 — Credentials and read-only exchange connection | `0.3.0` |
| 4 — Risk engine and order preparation | `0.4.0` |
| 5 — Demo execution | `0.5.0` |
| 6 — Live safety | `0.6.0` |
| 7 — Production-ready release | `1.0.0` |

## Development

Prerequisites:

- .NET 10 SDK

Build and test from the repository root:

```bash
dotnet build
dotnet test
```

Run the desktop shell:

```bash
dotnet run --project src/TradeRelay.Desktop
```

## Project structure

```text
src/TradeRelay.Desktop/         Avalonia shell and application composition
src/TradeRelay.Core/            Exchange-neutral models and settings
src/TradeRelay.Providers.Bybit/ Bybit adapter boundary
tests/TradeRelay.Tests/         Unit tests
```

## Disclaimer

TradeRelay is developer tooling, not financial advice. Trading involves risk. Users are responsible for API-key permissions, configuration, order review, and all resulting gains or losses. Use Demo mode first. Never use an API key with withdrawal permission.
