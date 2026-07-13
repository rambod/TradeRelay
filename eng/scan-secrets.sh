#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo_root"

if git grep -nEI '(BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY|api[_ -]?secret["[:space:]]*[:=]["[:space:]]*[A-Za-z0-9+/]{16,}|authorization:[[:space:]]*bearer[[:space:]]+[A-Fa-f0-9]{32,})' -- ':!eng/scan-secrets.sh' ':!tests/**'; then
  printf 'Potential committed secret detected.\n' >&2
  exit 1
fi

if git ls-files | grep -E '(^|/)(\.env|credentials\.|.*\.secrets\.json$|\.DS_Store$)' >/dev/null; then
  printf 'Forbidden secret or operating-system file is tracked.\n' >&2
  exit 1
fi

printf 'Secret-content scan passed.\n'
