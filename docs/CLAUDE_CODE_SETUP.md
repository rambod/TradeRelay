# Claude Code MCP Setup

TradeRelay is reachable only from the local machine. Claude web custom connectors cannot connect to localhost; use Claude Code or another local MCP client.

1. Start TradeRelay and its MCP server.
2. Set the token in the Claude Code process environment:

   ```bash
   export TRADERELAY_MCP_TOKEN='paste-the-current-token'
   ```

3. Add TradeRelay with a single-quoted header so your shell preserves the environment reference instead of expanding and persisting its value:

   ```bash
   claude mcp add --transport http traderelay http://127.0.0.1:5050/mcp --header 'Authorization: Bearer ${TRADERELAY_MCP_TOKEN}'
   ```

An equivalent project `.mcp.json` fragment is:

```json
{
  "mcpServers": {
    "traderelay": {
      "type": "http",
      "url": "http://127.0.0.1:5050/mcp",
      "headers": {
        "Authorization": "Bearer ${TRADERELAY_MCP_TOKEN}"
      }
    }
  }
}
```

Claude Code expands `${TRADERELAY_MCP_TOKEN}` from its environment at runtime. Do not replace it with the token value. Rotate the token in TradeRelay if it may have been exposed; the previous token becomes invalid immediately.
