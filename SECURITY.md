# Security Policy

## Supported version

Security fixes are provided for the latest `1.x` release. Upgrade to the newest patch before reporting a problem that may already be fixed.

## Private reporting

Do not open a public issue for a suspected vulnerability. Use this repository's **Private vulnerability reporting** form on GitHub: open the repository's Security tab, choose **Advisories**, then **Report a vulnerability**.

Include the affected version, platform, impact, minimal reproduction, and any suggested mitigation. Never include real exchange credentials, MCP tokens, authenticated payloads, signatures, or account data. Maintainers will acknowledge a complete report as soon as practical and coordinate remediation and disclosure through the private advisory.

## Safety guarantees and limits

TradeRelay binds MCP to loopback, uses bearer authentication, rejects withdrawal-enabled keys, begins each launch with writes disabled, centralizes write gating, and requires pre-action audit. Ambiguous order submissions are never blindly retried; they are reconciled through stream evidence and one REST lookup, or returned as `ORDER_STATE_UNKNOWN`.

These controls do not eliminate exchange, credential, network, market, operator, or software risk. If you suspect credential exposure, disable the API key at Bybit immediately before investigating the application.

Automated real-Live write testing is prohibited in this repository.
