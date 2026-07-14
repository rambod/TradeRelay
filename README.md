# TradeRelay

<p align="center">
  <img src="assets/branding/TradeRelay.png" alt="TradeRelay icon" width="112" height="112">
</p>

TradeRelay is a local desktop control plane for exchange accounts and MCP-capable agents. It lets an operator inspect market and account state, review risk, approve immutable trading plans, and audit every action without giving an agent unrestricted exchange access.

> [!CAUTION]
> Live trading can affect real funds. TradeRelay starts with all exchange writes disabled after every launch. Bybit is the only write-capable adapter; Binance and KuCoin are read-only.

## Download

Version **1.4.1** ships as portable, self-contained applications. No .NET installation is required.

Current version: `1.4.1`

| Platform | Architecture | Download |
| --- | --- | --- |
| macOS 14+ | Apple Silicon | [TradeRelay-1.4.1-osx-arm64.zip](https://github.com/rambod/TradeRelay/releases/latest/download/TradeRelay-1.4.1-osx-arm64.zip) |
| macOS 14+ | Intel | [TradeRelay-1.4.1-osx-x64.zip](https://github.com/rambod/TradeRelay/releases/latest/download/TradeRelay-1.4.1-osx-x64.zip) |
| Windows 11 | x64 | [TradeRelay-1.4.1-win-x64.zip](https://github.com/rambod/TradeRelay/releases/latest/download/TradeRelay-1.4.1-win-x64.zip) |
| Windows 11 | ARM64 | [TradeRelay-1.4.1-win-arm64.zip](https://github.com/rambod/TradeRelay/releases/latest/download/TradeRelay-1.4.1-win-arm64.zip) |
| Ubuntu 24.04 | x64 | [TradeRelay-1.4.1-linux-x64.tar.gz](https://github.com/rambod/TradeRelay/releases/latest/download/TradeRelay-1.4.1-linux-x64.tar.gz) |
| Ubuntu 24.04 | ARM64 | [TradeRelay-1.4.1-linux-arm64.tar.gz](https://github.com/rambod/TradeRelay/releases/latest/download/TradeRelay-1.4.1-linux-arm64.tar.gz) |

[Download checksums](https://github.com/rambod/TradeRelay/releases/latest/download/SHA256SUMS) · [View the latest release](https://github.com/rambod/TradeRelay/releases/latest)

Release metadata states whether each package is signed. Unsigned packages are supported, but the operating system may show an unknown-publisher warning. Verify `SHA256SUMS` before opening one.

## First run

1. Open TradeRelay. New trading actions are disabled.
2. Open **Connections** and connect an exchange account. Session-only credential storage is the default.
3. Review **Risk** settings before preparing an order.
4. Start the local MCP server from **Overview**.
5. Under **Connections → Agent clients**, install and pair Codex, Claude Code, or Gemini CLI.
6. Inspect account state and prepare plans before deliberately enabling Demo or Live Bybit trading.

Start with Bybit Demo when learning the workflow. Never use an API key with withdrawal permission.

## What TradeRelay provides

- A compact operator console for positions, orders, fills, protection, approvals, activity, and runtime errors.
- Concurrent read-only inspection of Bybit, Binance USD-M Futures, and KuCoin USDT Futures.
- Immutable, expiring prepared orders with instrument-aware normalization and account-risk checks.
- Desktop-only approval and session-only trading enablement.
- Loopback MCP over authenticated Streamable HTTP.
- OAuth pairing and direct per-user installation for Codex, Claude Code, and Gemini CLI.
- Append-only activity auditing, safe logs, reconciliation, and secret-free diagnostics.

TradeRelay is not a hosted trading service, strategy, signal provider, autonomous trader, or financial adviser. It does not blindly retry ambiguous submissions or automatically cancel orders when the application closes.

## Exchange capabilities

| Exchange | Environments | Read access | Write access |
| --- | --- | --- | --- |
| Bybit | Demo and Live | Market, account, positions, orders, history, executions, private stream | Guarded Demo and Live |
| Binance | Live | USD-M market, account, positions, orders, history, executions, private stream | Not supported |
| KuCoin | Live | USDT Futures market, account, positions, orders, history, executions, private stream | Not supported |

Supplying Binance or KuCoin to a write tool returns `CAPABILITY_NOT_SUPPORTED` before a write lease or provider call.

## Safety boundary

- The MCP server binds only to `127.0.0.1`.
- Every launch resets Demo and Live write enablement.
- Live enablement requires a desktop readiness review and the exact phrase `ENABLE LIVE TRADING`.
- Orders originate from approved, immutable prepared plans.
- Live cancel-all and position-close actions require a separate short-lived desktop confirmation.
- A successful pre-action audit write is mandatory before an exchange write.
- REST acknowledgements remain provisional until private-stream or REST reconciliation.
- `Disable New Trading Actions` blocks new writes; it does not cancel orders, close positions, or remove protection.

Read the full [security model](docs/SECURITY_MODEL.md) before enabling Live trading.

## Agent setup

TradeRelay detects supported local clients and previews every command and target before installation. New OAuth pairings receive `traderelay.read` and `traderelay.plan`; the `traderelay.trade` scope requires explicit re-pairing and still cannot bypass desktop safety controls.

- [Codex setup](docs/CODEX_SETUP.md)
- [Claude Code setup](docs/CLAUDE_CODE_SETUP.md)
- [Gemini CLI setup](docs/GEMINI_CLI_SETUP.md)
- [TradeRelay operator skill](integrations/skills/traderelay-operator/SKILL.md)

Legacy bearer authentication remains available under **Connections → Advanced compatibility**. The default endpoint is `http://127.0.0.1:5050/mcp`.

## Local data

TradeRelay has no telemetry or crash-upload service.

| Platform | Application data |
| --- | --- |
| macOS | `~/Library/Application Support/TradeRelay` |
| Windows | `%LOCALAPPDATA%\TradeRelay` |
| Linux | `~/.config/TradeRelay` |

Credentials and refresh tokens use protected operating-system storage when available. Settings, logs, diagnostics, and audit events exclude credentials, bearer tokens, signatures, authorization headers, raw authenticated payloads, exception messages, and stack traces.

## Development

Requirements: .NET SDK `10.0.301` from `global.json`.

```bash
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet format --no-restore --verify-no-changes
dotnet run --project src/TradeRelay.Desktop
```

Provider-specific payloads, transports, and SDK types remain inside `TradeRelay.Providers.*`. Core and MCP use exchange-neutral contracts. Bybit retains the only trading implementation.

See [development](docs/DEVELOPMENT.md), [contributing](CONTRIBUTING.md), [release procedure](docs/RELEASE.md), and the [changelog](CHANGELOG.md).

## License and disclaimer

TradeRelay is available under the [MIT License](LICENSE).

Trading involves substantial risk. Network failures, exchange behavior, slippage, gaps, liquidation, fees, funding, software defects, and operator error can cause loss. You remain responsible for credentials, configuration, approvals, exchange state, and all resulting gains or losses.
