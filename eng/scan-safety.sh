#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

unexpected_bybit="$(git grep -l 'using Bybit.Net' -- '*.cs' | grep -v '^src/TradeRelay.Providers.Bybit/' || true)"
if [[ -n "$unexpected_bybit" ]]; then
  printf 'Bybit.Net escaped the provider adapter:\n%s\n' "$unexpected_bybit" >&2
  exit 1
fi

if git grep -nE 'BYBIT_(LIVE|MAINNET).*WRITE|real Bybit Live write test' -- '.github/**' 'tests/**' 2>/dev/null; then
  printf 'Potential automated Live-write path detected.\n' >&2
  exit 1
fi

printf 'Trading write-path scan passed.\n'
