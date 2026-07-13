# Claude Code MCP and Skill Setup

TradeRelay is reachable only from the local machine. Claude web custom connectors cannot connect to localhost; use Claude Code.

## Preferred direct installation

1. Start TradeRelay and its MCP server.
2. Open **Connections → Agent clients** and select **Claude Code**.
3. Review the exact user-scoped HTTP MCP command and `~/.claude/skills/traderelay-operator` target.
4. Confirm and select **Install**.
5. Start Claude Code and approve its OAuth pairing in TradeRelay. New clients receive Read & Plan; Trade requires explicit re-pairing.

TradeRelay uses argument arrays, never places credentials or tokens in process arguments, and refuses conflicting `traderelay` entries. Uninstall removes only TradeRelay-owned files.

## Advanced legacy bearer setup

For a Claude Code version without OAuth pairing, set the token in its environment and preserve the reference with single quotes:

```bash
export TRADERELAY_MCP_TOKEN='paste-the-current-token'
claude mcp add --transport http --scope user traderelay http://127.0.0.1:5050/mcp --header 'Authorization: Bearer ${TRADERELAY_MCP_TOKEN}'
```

Claude Code expands `${TRADERELAY_MCP_TOKEN}` at runtime. Do not replace it with the token value. Rotate the token if it may have been exposed.
