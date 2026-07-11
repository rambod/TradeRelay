# TradeRelay

TradeRelay is a local desktop bridge intended to connect MCP-capable coding agents to controlled Bybit market-data and trading workflows.

> [!WARNING]
> TradeRelay is under active development. This repository currently contains only the Milestone 1 application scaffold. It must not be used for live trading.

## Current status

Milestone 1 provides:

- A .NET 10 solution with Avalonia desktop application, core, Bybit provider-boundary, and test projects
- A basic read-only Avalonia shell
- Generic hosting and dependency injection setup
- Initial exchange-neutral state enums and non-secret settings models
- xUnit scaffold tests

MCP hosting, credential handling, exchange connectivity, risk validation, approvals, and all trading behavior are intentionally not implemented yet.

Current version: `0.1.0`

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
