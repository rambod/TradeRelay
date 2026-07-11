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
dotnet run --project src/TradeRelay.App
```

## Project structure

```text
src/TradeRelay.App/             Avalonia shell and application composition
src/TradeRelay.Core/            Exchange-neutral models and settings
src/TradeRelay.Providers.Bybit/ Bybit adapter boundary
tests/TradeRelay.Tests/         Unit tests
```

## Disclaimer

TradeRelay is developer tooling, not financial advice. Trading involves risk. Users are responsible for API-key permissions, configuration, order review, and all resulting gains or losses. Use Demo mode first. Never use an API key with withdrawal permission.
