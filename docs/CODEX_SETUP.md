# Codex MCP and Skill Setup

TradeRelay uses Streamable HTTP on loopback. The default endpoint is `http://127.0.0.1:5050/mcp`.

## Preferred direct installation

1. Start TradeRelay and its MCP server.
2. Open **Connections → Agent clients** and select **Codex**.
3. Review the exact command and the `~/.agents/skills/traderelay-operator` target.
4. Confirm and select **Install**. TradeRelay invokes the native Codex MCP command with argument arrays and copies the canonical skill only into its owned directory.
5. Start Codex. Its OAuth browser flow creates a five-minute pairing request in TradeRelay.
6. Review the client, redirect, expiry, and scopes in Connections. Approve Read & Plan. Grant Trade only through an explicit re-pairing when required.

Repair repeats the reviewed owned installation. Uninstall removes the `traderelay` MCP entry and only the skill directory carrying TradeRelay's ownership marker. A conflicting entry or skill is never overwritten.

## Advanced legacy bearer setup

For a Codex version that cannot pair with OAuth, expose the compatibility token only to the Codex process:

```bash
export TRADERELAY_MCP_TOKEN='paste-the-current-token'
```

Then add this to `~/.codex/config.toml`:

```toml
[mcp_servers.traderelay]
url = "http://127.0.0.1:5050/mcp"
bearer_token_env_var = "TRADERELAY_MCP_TOKEN"
default_tools_approval_mode = "writes"
startup_timeout_sec = 10
tool_timeout_sec = 60
enabled = true
```

Never paste the token into configuration or chat. Before a write, use `get_system_status`, make exchange/environment explicit, prepare the immutable order, respect desktop approval, and reconcile after a single submission attempt.
