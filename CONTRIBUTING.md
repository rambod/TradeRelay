# Contributing to TradeRelay

Thank you for improving TradeRelay. Safety-sensitive changes should be small, reviewable, and backed by deterministic tests.

## Before you start

- Discuss large behavior or security changes in an issue first.
- Never commit credentials, tokens, account data, authenticated payloads, signatures, `.env` files, or real audit/log output.
- Keep Bybit.Net types inside `TradeRelay.Providers.Bybit` and exchange-neutral contracts in Core.
- Do not add databases, microservices, MediatR, repository layers, background trading queues, telemetry, persistent trading enablement, or a second execution path without an approved architecture change.
- Never add or run an automated real-Live write test.

## Development workflow

Install the SDK from `global.json`, then run:

```bash
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet format --no-restore --verify-no-changes
eng/scan-secrets.sh
eng/scan-safety.sh
```

Update lock files intentionally with `dotnet restore` when changing package references. Add tests for safety gates, failure paths, concurrency, redaction, and lifecycle behavior in proportion to the change.

## Pull requests

Explain the user-visible outcome, safety impact, verification performed, and any platform limitations. Keep unrelated changes out of the pull request. CI, platform builds, CodeQL, dependency review, formatting, vulnerability auditing, and safety scans must pass.

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md). Security findings belong in [private vulnerability reporting](SECURITY.md), not public issues.
