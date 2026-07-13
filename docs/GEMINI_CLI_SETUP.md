# Gemini CLI MCP and Skill Setup

## Preferred direct installation

1. Start TradeRelay and its MCP server.
2. Open **Connections → Agent clients** and select **Gemini CLI**.
3. Review the exact user-scoped HTTP MCP and skill-install commands plus the target paths.
4. Confirm and select **Install**.
5. Start Gemini CLI and approve its OAuth pairing in TradeRelay. New clients receive Read & Plan; Trade requires explicit re-pairing.

TradeRelay uses Gemini CLI's supported `mcp add --transport http --scope user` and `skills install` mechanisms. It never passes credentials or tokens in arguments, never silently rewrites unknown configuration, and stops on conflicting `traderelay` entries.

If the installed CLI lacks the required command, TradeRelay shows manual recovery guidance. Upgrade Gemini CLI and use Repair after reviewing the preview again.

The canonical `traderelay-operator` skill requires status-first inspection, explicit exchange/environment selection, desktop approvals, no automatic Live enablement, no blind retry after ambiguous submission, and post-write reconciliation.
