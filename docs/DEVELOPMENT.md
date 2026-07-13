# Development

## Toolchain

- .NET SDK `10.0.301` from `global.json`
- Avalonia `12.1.0`
- Rider, Visual Studio, VS Code, or the `dotnet` CLI
- macOS 14+, Windows 11 24H2+, or Ubuntu 24.04 for primary desktop validation

Restore is locked and package vulnerability auditing includes transitive dependencies:

```bash
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet format --no-restore --verify-no-changes
dotnet list TradeRelay.sln package --vulnerable --include-transitive
eng/scan-secrets.sh
eng/scan-safety.sh
```

Update lock files with a plain `dotnet restore` only after intentionally changing package references.

## Architecture boundaries

TradeRelay remains one desktop process. `TradeRelay.Desktop` owns Avalonia, the child Kestrel MCP host, application services, and desktop-only abstractions. Core contains exchange-neutral models, risk logic, immutable stores, and safety contracts. All Bybit.Net types remain in `TradeRelay.Providers.Bybit`.

Do not add a database, microservice, MediatR, repository layer, background trading queue, persistent trading enablement, telemetry, or another exchange adapter as incidental work.

The canonical cross-client Agent Skill lives at `integrations/skills/traderelay-operator`. Validate it after every change with the `skill-creator` quick validator before packaging. The desktop project includes this directory under `Skills/traderelay-operator` in build and publish output.

## Application data

Use `TRADERELAY_APP_DATA` to isolate local development or smoke-test state:

```bash
TRADERELAY_APP_DATA="$(mktemp -d)" dotnet run --project src/TradeRelay.Desktop
```

Without an override, data lives in `~/Library/Application Support/TradeRelay` on macOS, `%LOCALAPPDATA%\TradeRelay` on Windows, and `~/.config/TradeRelay` on Linux.

## Optional Bybit Demo integration

Read-only Demo acceptance reads credentials only from process environment variables:

```bash
export TRADERELAY_BYBIT_DEMO_API_KEY='...'
export TRADERELAY_BYBIT_DEMO_API_SECRET='...'
dotnet test --filter Category=BybitDemoIntegration
```

The smallest non-marketable Demo write/cancel test additionally requires `TRADERELAY_RUN_DEMO_TRADING_TESTS=1`. Use a separate Demo key and account. Normal tests never contact write endpoints. Real Bybit Live writes must never be automated.

## Linux development dependencies

On Ubuntu 24.04:

```bash
sudo apt install fontconfig libx11-6 libice6 libsm6 libxext6 libxrender1 libsecret-tools xvfb
```

CI runs packaged UI smoke tests under Xvfb.

## Branding assets

`assets/branding/TradeRelay.png` is the icon source. On macOS with ImageMagick installed, regenerate every committed PNG, ICO, and ICNS derivative with:

```bash
eng/generate-icons.sh
```

## Visual and accessibility acceptance

Check every page at 1080×720 and 900×640 using keyboard-only navigation. Verify visible focus, logical tab order, 44×44 targets, text labels for semantic states, masked secrets, readable loading/error/empty states, and no decorative animation.
