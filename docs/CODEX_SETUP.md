# Codex MCP Setup

TradeRelay uses Streamable HTTP on loopback with bearer authentication. The default endpoint is `http://127.0.0.1:5050/mcp`.

1. Start TradeRelay and start the MCP server.
2. Copy the current token and expose it only to the Codex process:

   ```bash
   export TRADERELAY_MCP_TOKEN='paste-the-current-token'
   ```

3. Add this to `~/.codex/config.toml`:

   ```toml
   [mcp_servers.traderelay]
   url = "http://127.0.0.1:5050/mcp"
   bearer_token_env_var = "TRADERELAY_MCP_TOKEN"
   default_tools_approval_mode = "writes"
   startup_timeout_sec = 10
   tool_timeout_sec = 60
   enabled = true
   ```

4. Restart Codex so it inherits the environment variable, then inspect MCP server/tool status.

If Settings changes the port, update `url`. Do not paste the token into `config.toml`, commit it, or send it to a remote client. The desktop **Copy Codex config** action produces this environment-based form and never embeds the value.

Before a write, inspect `get_system_status`, prepare the order, review its immutable fields, and follow desktop approval requirements. Never blindly retry an ambiguous submission.
